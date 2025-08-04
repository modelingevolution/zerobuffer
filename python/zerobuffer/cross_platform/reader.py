#!/usr/bin/env python3
"""
ZeroBuffer cross-platform test reader.

Reads frames from a buffer with standardized command-line interface.
"""

import argparse
import json
import sys
import time
import hashlib
from typing import Optional, Dict, Any

from zerobuffer import Reader


def verify_frame_data(data: bytes, frame_index: int, pattern: str) -> bool:
    """Verify frame data matches expected pattern."""
    if pattern == "sequential":
        for i in range(len(data)):
            expected = (frame_index + i) % 256
            if data[i] != expected:
                return False
        return True
    elif pattern == "random":
        import random
        random.seed(frame_index)
        for i in range(len(data)):
            expected = random.randint(0, 255)
            if data[i] != expected:
                return False
        return True
    elif pattern == "zero":
        return all(b == 0 for b in data)
    elif pattern == "ones":
        return all(b == 0xFF for b in data)
    elif pattern == "none":
        return True  # No verification
    else:
        raise ValueError(f"Unknown pattern: {pattern}")


def main():
    parser = argparse.ArgumentParser(
        description="Read frames from a ZeroBuffer for testing",
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    
    parser.add_argument("buffer_name", help="Name of the buffer to read from")
    parser.add_argument("-n", "--frames", type=int, default=1000,
                        help="Number of frames to read (default: 1000, -1 for unlimited)")
    parser.add_argument("-s", "--size", type=int, default=1024,
                        help="Expected size of each frame in bytes (default: 1024)")
    parser.add_argument("--timeout-ms", type=int, default=5000,
                        help="Timeout for frame reads in milliseconds (default: 5000)")
    parser.add_argument("--verify", choices=["none", "sequential", "random", "zero", "ones"],
                        default="none",
                        help="Verify data pattern (default: none)")
    parser.add_argument("--checksum", action="store_true",
                        help="Calculate checksums for each frame")
    parser.add_argument("--batch-size", type=int, default=1,
                        help="Read frames in batches (default: 1)")
    parser.add_argument("--json-output", action="store_true",
                        help="Output results in JSON format")
    parser.add_argument("-v", "--verbose", action="store_true",
                        help="Verbose output")
    
    args = parser.parse_args()
    
    result = {
        "operation": "read",
        "buffer_name": args.buffer_name,
        "frames_read": 0,
        "frame_size": args.size,
        "metadata_size": 0,
        "duration_seconds": 0.0,
        "throughput_mbps": 0.0,
        "verification_errors": 0,
        "checksums": [],
        "errors": []
    }
    
    try:
        if args.verbose and not args.json_output:
            print(f"Connecting to buffer: {args.buffer_name}")
        
        reader = Reader(args.buffer_name)
        
        # Read metadata if available
        metadata = reader.read_metadata()
        if metadata:
            result["metadata_size"] = len(metadata)
            if args.verbose and not args.json_output:
                print(f"Read metadata: {len(metadata)} bytes")
        
        # Read frames
        start_time = time.time()
        frames_to_read = args.frames if args.frames >= 0 else float('inf')
        frame_index = 0
        
        while frame_index < frames_to_read:
            try:
                # Read frame with timeout
                frame = reader.read_frame(timeout_ms=args.timeout_ms)
                
                if frame is None:
                    # Timeout or no more frames
                    if args.verbose and not args.json_output:
                        print(f"No more frames after {frame_index}")
                    break
                
                # Verify frame size
                if len(frame) != args.size:
                    result["errors"].append(
                        f"Frame {frame_index}: Expected size {args.size}, got {len(frame)}"
                    )
                
                # Verify data pattern if requested
                if args.verify != "none":
                    if not verify_frame_data(frame, frame_index, args.verify):
                        result["verification_errors"] += 1
                        if args.verbose and not args.json_output:
                            print(f"Frame {frame_index}: Verification failed")
                
                # Calculate checksum if requested
                if args.checksum:
                    checksum = hashlib.md5(frame).hexdigest()
                    if len(result["checksums"]) < 100:  # Limit stored checksums
                        result["checksums"].append({
                            "frame": frame_index,
                            "checksum": checksum
                        })
                
                frame_index += 1
                result["frames_read"] = frame_index
                
                if args.verbose and not args.json_output and frame_index % 100 == 0:
                    print(f"Read {frame_index} frames...")
                
            except Exception as e:
                result["errors"].append(f"Frame {frame_index}: {str(e)}")
                break
        
        end_time = time.time()
        duration = end_time - start_time
        result["duration_seconds"] = duration
        
        # Calculate throughput
        total_mb = (result["frames_read"] * args.size) / (1024.0 * 1024.0)
        throughput = total_mb / duration if duration > 0 else 0
        result["throughput_mbps"] = throughput
        
        if not args.json_output:
            print(f"Read {result['frames_read']} frames in {duration:.2f} seconds")
            print(f"Throughput: {throughput:.2f} MB/s")
            if args.verify != "none":
                print(f"Verification errors: {result['verification_errors']}")
        
        reader.close()
        
    except Exception as e:
        result["errors"].append(str(e))
        
        if args.json_output:
            print(json.dumps(result, indent=2))
        else:
            print(f"Error: {e}", file=sys.stderr)
        
        return 2
    
    if args.json_output:
        print(json.dumps(result, indent=2))
    
    return 0 if result["verification_errors"] == 0 and not result["errors"] else 1


if __name__ == "__main__":
    sys.exit(main())