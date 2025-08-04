#!/usr/bin/env python3
"""
ZeroBuffer Writer CLI - matches C# implementation

Usage: zerobuffer_writer <buffer_name> [options]
"""

import sys
import json
import time
import argparse
import hashlib
from pathlib import Path
from zerobuffer import Writer, WriterDeadException


def create_pattern_data(pattern: str, size: int) -> bytes:
    """Create data based on pattern type"""
    if pattern == 'sequential':
        # Create sequential pattern (0, 1, 2, ... 255, 0, 1, ...)
        data = bytearray(size)
        for i in range(size):
            data[i] = i % 256
        return bytes(data)
    elif pattern == 'zeros':
        return bytes(size)
    elif pattern == 'random':
        import random
        return bytes(random.randint(0, 255) for _ in range(size))
    elif pattern == 'ones':
        return bytes([255] * size)
    else:
        # Default to sequential
        return create_pattern_data('sequential', size)


def main():
    parser = argparse.ArgumentParser(description='ZeroBuffer writer')
    parser.add_argument('buffer_name', help='Buffer name')
    parser.add_argument('-n', '--frames', type=int, default=1000, 
                       help='Number of frames to write (default: 1000)')
    parser.add_argument('-s', '--size', type=int, default=1024,
                       help='Frame size in bytes (default: 1024)')
    parser.add_argument('-m', '--metadata', type=str,
                       help='Metadata string to write')
    parser.add_argument('--metadata-file', type=str,
                       help='File containing metadata to write')
    parser.add_argument('--pattern', choices=['sequential', 'zeros', 'random', 'ones'],
                       default='sequential', help='Data pattern (default: sequential)')
    parser.add_argument('--delay-ms', type=int, default=0,
                       help='Delay between frames in milliseconds (default: 0)')
    parser.add_argument('--batch-size', type=int, default=1,
                       help='Number of frames to write in each batch (default: 1)')
    parser.add_argument('--json-output', action='store_true',
                       help='Output results as JSON')
    parser.add_argument('-v', '--verbose', action='store_true',
                       help='Verbose output')
    parser.add_argument('--exit-frame', action='store_true',
                       help='Write EXIT frame at end')
    
    args = parser.parse_args()
    
    # Prepare frame data
    frame_data = create_pattern_data(args.pattern, args.size)
    frame_hash = hashlib.sha256(frame_data).hexdigest()
    
    # Prepare metadata
    metadata = None
    if args.metadata:
        metadata = args.metadata.encode('utf-8')
    elif args.metadata_file:
        metadata_path = Path(args.metadata_file)
        if metadata_path.exists():
            metadata = metadata_path.read_bytes()
        else:
            print(f"Error: Metadata file not found: {args.metadata_file}", file=sys.stderr)
            sys.exit(1)
    
    # Statistics
    frames_written = 0
    bytes_written = 0
    start_time = time.time()
    
    try:
        # Connect to buffer
        with Writer(args.buffer_name) as writer:
            if args.verbose and not args.json_output:
                print(f"Connected to buffer: {args.buffer_name}")
                print(f"Pattern: {args.pattern}, Size: {args.size}, Hash: {frame_hash}")
            
            # Write metadata if provided
            if metadata:
                writer.set_metadata(metadata)
                if args.verbose and not args.json_output:
                    print(f"Wrote metadata: {len(metadata)} bytes")
            
            # Write frames
            for i in range(0, args.frames, args.batch_size):
                batch_start = i
                batch_end = min(i + args.batch_size, args.frames)
                
                for j in range(batch_start, batch_end):
                    writer.write_frame(frame_data)
                    frames_written += 1
                    bytes_written += len(frame_data)
                    
                    if args.verbose and not args.json_output and (j + 1) % 100 == 0:
                        elapsed = time.time() - start_time
                        fps = frames_written / elapsed if elapsed > 0 else 0
                        print(f"Progress: {j+1}/{args.frames} frames, {fps:.1f} fps")
                
                # Delay between batches if specified
                if args.delay_ms > 0 and batch_end < args.frames:
                    time.sleep(args.delay_ms / 1000.0)
            
            # Write exit frame if requested
            if args.exit_frame:
                writer.write_frame(b"EXIT")
                if args.verbose and not args.json_output:
                    print("Wrote EXIT frame")
            
            # Calculate statistics
            end_time = time.time()
            duration = end_time - start_time
            fps = frames_written / duration if duration > 0 else 0
            throughput_mbps = (bytes_written / duration / 1024 / 1024) if duration > 0 else 0
            
            if args.json_output:
                # Output JSON results
                result = {
                    "buffer_name": args.buffer_name,
                    "frames_written": frames_written,
                    "bytes_written": bytes_written,
                    "duration_seconds": duration,
                    "fps": fps,
                    "throughput_mbps": throughput_mbps,
                    "frame_size": args.size,
                    "pattern": args.pattern,
                    "frame_hash": frame_hash,
                    "metadata_size": len(metadata) if metadata else 0
                }
                print(json.dumps(result, indent=2))
            else:
                print(f"\nCompleted:")
                print(f"  Frames written: {frames_written}")
                print(f"  Bytes written: {bytes_written:,}")
                print(f"  Duration: {duration:.2f}s")
                print(f"  FPS: {fps:.1f}")
                print(f"  Throughput: {throughput_mbps:.1f} MB/s")
                
    except WriterDeadException:
        if not args.json_output:
            print("Error: Reader disconnected", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        if args.json_output:
            error_result = {
                "error": str(e),
                "frames_written": frames_written,
                "bytes_written": bytes_written
            }
            print(json.dumps(error_result, indent=2))
        else:
            print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()