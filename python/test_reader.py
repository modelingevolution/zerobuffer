#!/usr/bin/env python3
"""
ZeroBuffer Test Reader - reads and verifies frames for cross-platform testing

Usage: python test_reader.py <buffer_name> [options]
"""

import sys
import time
import argparse
import hashlib
from zerobuffer import Reader, BufferConfig, ReaderDeadException


def main():
    parser = argparse.ArgumentParser(description='ZeroBuffer test reader')
    parser.add_argument('buffer_name', help='Buffer name')
    parser.add_argument('--metadata-size', type=int, default=4096, help='Metadata size (default: 4096)')
    parser.add_argument('--payload-size', type=int, default=256*1024*1024, help='Payload size (default: 256MB)')
    parser.add_argument('--expected-frames', type=int, default=300, help='Expected number of frames (default: 300)')
    parser.add_argument('--verify', action='store_true', help='Verify frame content')
    parser.add_argument('--exit-on-exit-frame', action='store_true', help='Exit when EXIT frame received')
    parser.add_argument('--timeout', type=float, default=5.0, help='Read timeout in seconds (default: 5.0)')
    
    args = parser.parse_args()
    
    print(f"Starting reader: buffer={args.buffer_name}, expected={args.expected_frames}", flush=True)
    
    config = BufferConfig(metadata_size=args.metadata_size, payload_size=args.payload_size)
    frames_read = 0
    errors = 0
    
    try:
        # Create buffer
        with Reader(args.buffer_name, config) as reader:
            print("Buffer created, waiting for writer...", flush=True)
            
            # Wait for writer
            if not reader.is_writer_connected(timeout_ms=10000):  # 10 second timeout
                print("Timeout waiting for writer", file=sys.stderr)
                sys.exit(1)
            
            print("Writer connected", flush=True)
            
            # Read metadata
            metadata = reader.get_metadata()
            if metadata:
                metadata_str = bytes(metadata).decode('utf-8', errors='ignore')
                print(f"Metadata: {metadata_str}", flush=True)
                
                # Parse expected hash if present
                expected_hash = None
                if 'hash=' in metadata_str:
                    expected_hash = metadata_str.split('hash=')[1].split('|')[0]
                    print(f"Expected frame hash: {expected_hash}", flush=True)
            
            # Read frames
            start_time = time.time()
            last_report_time = start_time
            
            while True:
                try:
                    frame = reader.read_frame(timeout=args.timeout)
                    if frame is None:
                        if frames_read >= args.expected_frames:
                            break
                        continue
                    
                    frame_data = bytes(frame.data)
                    
                    # Check for exit frame
                    if args.exit_on_exit_frame and frame_data == b"EXIT":
                        print("Received EXIT frame", flush=True)
                        reader.release_frame(frame)
                        break
                    
                    frames_read += 1
                    
                    # Verify frame if requested
                    if args.verify and expected_hash:
                        actual_hash = hashlib.sha256(frame_data).hexdigest()
                        if actual_hash != expected_hash:
                            errors += 1
                            print(f"Frame {frames_read} hash mismatch: expected {expected_hash}, got {actual_hash}", 
                                  file=sys.stderr, flush=True)
                    
                    reader.release_frame(frame)
                    
                    # Progress report every second
                    current_time = time.time()
                    if current_time - last_report_time >= 1.0:
                        elapsed = current_time - start_time
                        fps = frames_read / elapsed if elapsed > 0 else 0
                        print(f"Progress: {frames_read}/{args.expected_frames} frames, {fps:.1f} fps", flush=True)
                        last_report_time = current_time
                    
                except ReaderDeadException:
                    print("Writer disconnected", flush=True)
                    break
            
            # Final stats
            total_time = time.time() - start_time
            avg_fps = frames_read / total_time if total_time > 0 else 0
            
            print(f"Completed: {frames_read} frames in {total_time:.2f}s ({avg_fps:.1f} fps)", flush=True)
            if errors > 0:
                print(f"Verification errors: {errors}", file=sys.stderr)
                sys.exit(1)
            
            if frames_read < args.expected_frames:
                print(f"Warning: Expected {args.expected_frames} frames but got {frames_read}", file=sys.stderr)
                sys.exit(1)
                
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()