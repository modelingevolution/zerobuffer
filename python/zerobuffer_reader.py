#!/usr/bin/env python3
"""
ZeroBuffer Reader CLI - matches C# implementation

Usage: zerobuffer_reader <buffer_name> [options]
"""

import sys
import json
import time
import argparse
import hashlib
from zerobuffer import Reader, BufferConfig, ReaderDeadException


def verify_frame(frame_data: bytes, verify_mode: str, expected_pattern: str = 'sequential') -> bool:
    """Verify frame data based on verification mode"""
    if verify_mode == 'none':
        return True
    elif verify_mode == 'pattern':
        # Verify sequential pattern
        if expected_pattern == 'sequential':
            for i, byte in enumerate(frame_data):
                if byte != i % 256:
                    return False
        elif expected_pattern == 'zeros':
            return all(b == 0 for b in frame_data)
        elif expected_pattern == 'ones':
            return all(b == 255 for b in frame_data)
        return True
    elif verify_mode == 'checksum':
        # For checksum mode, we'd need the expected checksum passed somehow
        # For now, just return True
        return True
    return True


def main():
    parser = argparse.ArgumentParser(description='ZeroBuffer reader')
    parser.add_argument('buffer_name', help='Buffer name')
    parser.add_argument('-n', '--frames', type=int, default=0,
                       help='Number of frames to read (0 for unlimited, default: 0)')
    parser.add_argument('-s', '--size', type=int, default=1024,
                       help='Expected frame size in bytes (default: 1024)')
    parser.add_argument('--timeout-ms', type=int, default=5000,
                       help='Read timeout in milliseconds (default: 5000)')
    parser.add_argument('--verify', choices=['none', 'pattern', 'checksum'],
                       default='none', help='Verification mode (default: none)')
    parser.add_argument('--checksum', action='store_true',
                       help='Calculate and display checksums')
    parser.add_argument('--batch-size', type=int, default=1,
                       help='Number of frames to read in each batch (default: 1)')
    parser.add_argument('--json-output', action='store_true',
                       help='Output results as JSON')
    parser.add_argument('-v', '--verbose', action='store_true',
                       help='Verbose output')
    parser.add_argument('--metadata-size', type=int, default=4096,
                       help='Metadata size (default: 4096)')
    parser.add_argument('--payload-size', type=int, default=256*1024*1024,
                       help='Payload size (default: 256MB)')
    parser.add_argument('--exit-on-exit-frame', action='store_true',
                       help='Exit when EXIT frame received')
    
    args = parser.parse_args()
    
    # Statistics
    frames_read = 0
    bytes_read = 0
    errors = 0
    checksums = []
    start_time = time.time()
    
    # Create buffer configuration
    config = BufferConfig(metadata_size=args.metadata_size, payload_size=args.payload_size)
    
    try:
        # Create buffer
        with Reader(args.buffer_name, config) as reader:
            if args.verbose and not args.json_output:
                print(f"Buffer created: {args.buffer_name}")
                print("Waiting for writer...")
            
            # Wait for writer
            if not reader.is_writer_connected(timeout_ms=30000):  # 30 second timeout
                if not args.json_output:
                    print("Timeout waiting for writer", file=sys.stderr)
                sys.exit(1)
            
            if args.verbose and not args.json_output:
                print("Writer connected")
            
            # Read metadata
            metadata = reader.get_metadata()
            metadata_str = ""
            if metadata:
                metadata_str = bytes(metadata).decode('utf-8', errors='ignore')
                if args.verbose and not args.json_output:
                    print(f"Metadata: {metadata_str}")
            
            # Read frames
            last_report_time = start_time
            frame_count = 0
            
            while args.frames == 0 or frames_read < args.frames:
                try:
                    # Read frame with timeout
                    frame = reader.read_frame(timeout=args.timeout_ms / 1000.0)
                    if frame is None:
                        continue
                    
                    frame_data = bytes(frame.data)
                    
                    # Check for exit frame
                    if args.exit_on_exit_frame and frame_data == b"EXIT":
                        if args.verbose and not args.json_output:
                            print("Received EXIT frame")
                        break
                    
                    frames_read += 1
                    bytes_read += len(frame_data)
                    
                    # Verify frame if requested
                    if args.verify != 'none':
                        if not verify_frame(frame_data, args.verify):
                            errors += 1
                            if args.verbose and not args.json_output:
                                print(f"Frame {frames_read} verification failed")
                    
                    # Calculate checksum if requested
                    if args.checksum:
                        checksum = hashlib.sha256(frame_data).hexdigest()
                        checksums.append(checksum)
                    
                    # Size check
                    if args.size > 0 and len(frame_data) != args.size:
                        errors += 1
                        if args.verbose and not args.json_output:
                            print(f"Frame {frames_read} size mismatch: expected {args.size}, got {len(frame_data)}")
                    
                    # Progress report
                    if args.verbose and not args.json_output:
                        current_time = time.time()
                        if current_time - last_report_time >= 1.0 or frames_read % 100 == 0:
                            elapsed = current_time - start_time
                            fps = frames_read / elapsed if elapsed > 0 else 0
                            print(f"Progress: {frames_read} frames, {fps:.1f} fps")
                            last_report_time = current_time
                    
                except ReaderDeadException:
                    if args.verbose and not args.json_output:
                        print("Writer disconnected")
                    break
            
            # Calculate statistics
            end_time = time.time()
            duration = end_time - start_time
            fps = frames_read / duration if duration > 0 else 0
            throughput_mbps = (bytes_read / duration / 1024 / 1024) if duration > 0 else 0
            
            if args.json_output:
                # Output JSON results
                result = {
                    "buffer_name": args.buffer_name,
                    "frames_read": frames_read,
                    "bytes_read": bytes_read,
                    "duration_seconds": duration,
                    "fps": fps,
                    "throughput_mbps": throughput_mbps,
                    "errors": errors,
                    "metadata": metadata_str
                }
                if args.checksum and checksums:
                    result["checksums"] = checksums[:10]  # First 10 checksums
                    if len(checksums) > 10:
                        result["checksum_count"] = len(checksums)
                print(json.dumps(result, indent=2))
            else:
                print(f"\nCompleted:")
                print(f"  Frames read: {frames_read}")
                print(f"  Bytes read: {bytes_read:,}")
                print(f"  Duration: {duration:.2f}s")
                print(f"  FPS: {fps:.1f}")
                print(f"  Throughput: {throughput_mbps:.1f} MB/s")
                if errors > 0:
                    print(f"  Errors: {errors}")
                
    except Exception as e:
        if args.json_output:
            error_result = {
                "error": str(e),
                "frames_read": frames_read,
                "bytes_read": bytes_read
            }
            print(json.dumps(error_result, indent=2))
        else:
            print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()