#!/usr/bin/env python3
"""
ZeroBuffer cross-platform test writer.

Writes frames to a buffer with standardized command-line interface.
"""

import argparse
import json
import sys
import time
import random
from typing import Optional, List

from zerobuffer import Writer


def fill_frame_data(data: bytearray, frame_index: int, pattern: str) -> None:
    """Fill frame data with specified pattern."""
    if pattern == "sequential":
        for i in range(len(data)):
            data[i] = (frame_index + i) % 256
    elif pattern == "random":
        random.seed(frame_index)
        for i in range(len(data)):
            data[i] = random.randint(0, 255)
    elif pattern == "zero":
        data[:] = b'\x00' * len(data)
    elif pattern == "ones":
        data[:] = b'\xff' * len(data)
    else:
        raise ValueError(f"Unknown pattern: {pattern}")


def main():
    parser = argparse.ArgumentParser(
        description="Write frames to a ZeroBuffer for testing",
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    
    parser.add_argument("buffer_name", help="Name of the buffer to write to")
    parser.add_argument("-n", "--frames", type=int, default=1000,
                        help="Number of frames to write (default: 1000)")
    parser.add_argument("-s", "--size", type=int, default=1024,
                        help="Size of each frame in bytes (default: 1024)")
    parser.add_argument("-m", "--metadata", type=str,
                        help="Metadata to write")
    parser.add_argument("--metadata-file", type=str,
                        help="Read metadata from file")
    parser.add_argument("--pattern", choices=["sequential", "random", "zero", "ones"],
                        default="sequential",
                        help="Data pattern (default: sequential)")
    parser.add_argument("--delay-ms", type=int, default=0,
                        help="Delay between frames in milliseconds (default: 0)")
    parser.add_argument("--batch-size", type=int, default=1,
                        help="Write frames in batches (default: 1)")
    parser.add_argument("--json-output", action="store_true",
                        help="Output results in JSON format")
    parser.add_argument("-v", "--verbose", action="store_true",
                        help="Verbose output")
    
    args = parser.parse_args()
    
    result = {
        "operation": "write",
        "buffer_name": args.buffer_name,
        "frames_written": 0,
        "frame_size": args.size,
        "metadata_size": 0,
        "duration_seconds": 0.0,
        "throughput_mbps": 0.0,
        "errors": []
    }
    
    try:
        if args.verbose and not args.json_output:
            print(f"Connecting to buffer: {args.buffer_name}")
        
        writer = Writer(args.buffer_name)
        
        # Write metadata if provided
        metadata = None
        if args.metadata:
            metadata = args.metadata.encode('utf-8')
        elif args.metadata_file:
            with open(args.metadata_file, 'rb') as f:
                metadata = f.read()
        
        if metadata:
            writer.write_metadata(metadata)
            result["metadata_size"] = len(metadata)
            if args.verbose and not args.json_output:
                print(f"Wrote metadata: {len(metadata)} bytes")
        
        # Prepare frame data
        frame_data = bytearray(args.size)
        
        # Write frames
        start_time = time.time()
        
        for i in range(args.frames):
            fill_frame_data(frame_data, i, args.pattern)
            writer.write_frame(bytes(frame_data))
            result["frames_written"] = i + 1
            
            if args.verbose and not args.json_output and (i + 1) % 100 == 0:
                print(f"Wrote {i + 1} frames...")
            
            if args.delay_ms > 0:
                time.sleep(args.delay_ms / 1000.0)
        
        end_time = time.time()
        duration = end_time - start_time
        result["duration_seconds"] = duration
        
        # Calculate throughput
        total_mb = (args.frames * args.size) / (1024.0 * 1024.0)
        throughput = total_mb / duration if duration > 0 else 0
        result["throughput_mbps"] = throughput
        
        if not args.json_output:
            print(f"Wrote {args.frames} frames in {duration:.2f} seconds")
            print(f"Throughput: {throughput:.2f} MB/s")
        
        writer.close()
        
    except Exception as e:
        result["errors"].append(str(e))
        
        if args.json_output:
            print(json.dumps(result, indent=2))
        else:
            print(f"Error: {e}", file=sys.stderr)
        
        return 2
    
    if args.json_output:
        print(json.dumps(result, indent=2))
    
    return 0


if __name__ == "__main__":
    sys.exit(main())