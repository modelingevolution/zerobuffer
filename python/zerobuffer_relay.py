#!/usr/bin/env python3
"""
ZeroBuffer Relay CLI - matches C# implementation

Usage: zerobuffer_relay <input_buffer> <output_buffer> [options]
"""

import sys
import json
import time
import argparse
from zerobuffer import Reader, Writer, BufferConfig, ReaderDeadException, WriterDeadException


def transform_data(data: bytes, transform: str, xor_key: int = 255) -> bytes:
    """Apply transformation to data"""
    if transform == 'none':
        return data
    elif transform == 'xor':
        # XOR transformation
        return bytes(b ^ xor_key for b in data)
    elif transform == 'reverse':
        # Reverse bytes
        return data[::-1]
    elif transform == 'upper':
        # Convert lowercase to uppercase (for text data)
        return data.upper()
    else:
        return data


def main():
    parser = argparse.ArgumentParser(description='ZeroBuffer relay')
    parser.add_argument('input_buffer', help='Input buffer name')
    parser.add_argument('output_buffer', help='Output buffer name')
    parser.add_argument('-n', '--frames', type=int, default=0,
                       help='Number of frames to relay (0 for unlimited, default: 0)')
    parser.add_argument('--create-output', action='store_true',
                       help='Create output buffer (be the reader)')
    parser.add_argument('--buffer-size', type=int, default=256*1024*1024,
                       help='Buffer size in bytes (default: 256MB)')
    parser.add_argument('--timeout-ms', type=int, default=5000,
                       help='Read timeout in milliseconds (default: 5000)')
    parser.add_argument('--transform', choices=['none', 'xor', 'reverse', 'upper'],
                       default='none', help='Transformation to apply (default: none)')
    parser.add_argument('--xor-key', type=int, default=255,
                       help='XOR key for transformation (default: 255)')
    parser.add_argument('--log-interval', type=int, default=100,
                       help='Progress log interval (default: 100)')
    parser.add_argument('--json-output', action='store_true',
                       help='Output results as JSON')
    parser.add_argument('-v', '--verbose', action='store_true',
                       help='Verbose output')
    parser.add_argument('--metadata-size', type=int, default=4096,
                       help='Metadata size (default: 4096)')
    
    args = parser.parse_args()
    
    # Statistics
    frames_relayed = 0
    bytes_relayed = 0
    errors = 0
    start_time = time.time()
    
    # Create buffer configuration
    config = BufferConfig(metadata_size=args.metadata_size, payload_size=args.buffer_size)
    
    try:
        if args.create_output:
            # We create the output buffer (act as reader for output)
            if args.verbose and not args.json_output:
                print(f"Creating output buffer: {args.output_buffer}")
            
            with Reader(args.output_buffer, config) as output_reader:
                # Wait for output writer to connect
                if args.verbose and not args.json_output:
                    print("Waiting for output writer...")
                
                if not output_reader.is_writer_connected(timeout_ms=30000):
                    if not args.json_output:
                        print("Timeout waiting for output writer", file=sys.stderr)
                    sys.exit(1)
                
                # Now connect to input as writer
                with Writer(args.input_buffer) as input_writer:
                    if args.verbose and not args.json_output:
                        print(f"Connected to input buffer: {args.input_buffer}")
                        print("Relay started (create-output mode)")
                    
                    # In this mode, we read from output and write to input (reverse relay)
                    # This is less common but supported
                    while args.frames == 0 or frames_relayed < args.frames:
                        try:
                            frame = output_reader.read_frame(timeout=args.timeout_ms / 1000.0)
                            if frame is None:
                                continue
                            
                            frame_data = bytes(frame.data)
                            transformed = transform_data(frame_data, args.transform, args.xor_key)
                            
                            input_writer.write_frame(transformed)
                            frames_relayed += 1
                            bytes_relayed += len(frame_data)
                            
                            if args.verbose and not args.json_output and frames_relayed % args.log_interval == 0:
                                elapsed = time.time() - start_time
                                fps = frames_relayed / elapsed if elapsed > 0 else 0
                                print(f"Progress: {frames_relayed} frames, {fps:.1f} fps")
                                
                        except (ReaderDeadException, WriterDeadException):
                            if args.verbose and not args.json_output:
                                print("Connection lost")
                            break
        else:
            # Normal mode: read from input, write to output
            with Reader(args.input_buffer, config) as reader:
                if args.verbose and not args.json_output:
                    print(f"Connected to input buffer: {args.input_buffer}")
                    print("Waiting for input writer...")
                
                # Wait for writer to connect to input
                if not reader.is_writer_connected(timeout_ms=30000):
                    if not args.json_output:
                        print("Timeout waiting for input writer", file=sys.stderr)
                    sys.exit(1)
                
                # Get metadata from input
                metadata = reader.get_metadata()
                
                # Connect to output buffer
                writer = None
                connect_attempts = 0
                max_attempts = 50  # 5 seconds with 100ms sleep
                
                while connect_attempts < max_attempts:
                    try:
                        writer = Writer(args.output_buffer)
                        break
                    except:
                        connect_attempts += 1
                        time.sleep(0.1)
                
                if writer is None:
                    if not args.json_output:
                        print(f"Failed to connect to output buffer: {args.output_buffer}", file=sys.stderr)
                    sys.exit(1)
                
                with writer:
                    # Copy metadata if present
                    if metadata:
                        writer.set_metadata(bytes(metadata))
                    
                    if args.verbose and not args.json_output:
                        print(f"Connected to output buffer: {args.output_buffer}")
                        print("Relay started")
                    
                    # Relay frames
                    last_report_time = start_time
                    
                    while args.frames == 0 or frames_relayed < args.frames:
                        try:
                            frame = reader.read_frame(timeout=args.timeout_ms / 1000.0)
                            if frame is None:
                                continue
                            
                            frame_data = bytes(frame.data)
                            
                            # Apply transformation
                            transformed = transform_data(frame_data, args.transform, args.xor_key)
                            
                            # Write to output
                            writer.write_frame(transformed)
                            frames_relayed += 1
                            bytes_relayed += len(frame_data)
                            
                            # Progress report
                            if args.verbose and not args.json_output:
                                current_time = time.time()
                                if frames_relayed % args.log_interval == 0 or current_time - last_report_time >= 1.0:
                                    elapsed = current_time - start_time
                                    fps = frames_relayed / elapsed if elapsed > 0 else 0
                                    print(f"Progress: {frames_relayed} frames, {fps:.1f} fps")
                                    last_report_time = current_time
                                    
                        except ReaderDeadException:
                            if args.verbose and not args.json_output:
                                print("Input writer disconnected")
                            break
                        except WriterDeadException:
                            if args.verbose and not args.json_output:
                                print("Output reader disconnected")
                            break
        
        # Calculate statistics
        end_time = time.time()
        duration = end_time - start_time
        fps = frames_relayed / duration if duration > 0 else 0
        throughput_mbps = (bytes_relayed / duration / 1024 / 1024) if duration > 0 else 0
        
        if args.json_output:
            # Output JSON results
            result = {
                "input_buffer": args.input_buffer,
                "output_buffer": args.output_buffer,
                "frames_relayed": frames_relayed,
                "bytes_relayed": bytes_relayed,
                "duration_seconds": duration,
                "fps": fps,
                "throughput_mbps": throughput_mbps,
                "transform": args.transform,
                "errors": errors
            }
            print(json.dumps(result, indent=2))
        else:
            print(f"\nCompleted:")
            print(f"  Frames relayed: {frames_relayed}")
            print(f"  Bytes relayed: {bytes_relayed:,}")
            print(f"  Duration: {duration:.2f}s")
            print(f"  FPS: {fps:.1f}")
            print(f"  Throughput: {throughput_mbps:.1f} MB/s")
            if args.transform != 'none':
                print(f"  Transform: {args.transform}")
                
    except Exception as e:
        if args.json_output:
            error_result = {
                "error": str(e),
                "frames_relayed": frames_relayed,
                "bytes_relayed": bytes_relayed
            }
            print(json.dumps(error_result, indent=2))
        else:
            print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()