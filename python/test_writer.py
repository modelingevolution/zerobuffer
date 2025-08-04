#!/usr/bin/env python3
"""
ZeroBuffer Test Writer - writes frames for cross-platform testing

Usage: python test_writer.py <buffer_name> [options]
"""

import sys
import time
import argparse
import hashlib
from zerobuffer import Writer, BufferConfig


def main():
    parser = argparse.ArgumentParser(description='ZeroBuffer test writer')
    parser.add_argument('buffer_name', help='Buffer name')
    parser.add_argument('--metadata-size', type=int, default=4096, help='Metadata size (default: 4096)')
    parser.add_argument('--payload-size', type=int, default=256*1024*1024, help='Payload size (default: 256MB)')
    parser.add_argument('--frame-size', type=int, default=3225600, help='Frame size (default: 3.1MB for 1920x1080 YUV420)')
    parser.add_argument('--num-frames', type=int, default=300, help='Number of frames to write (default: 300)')
    parser.add_argument('--fps', type=int, default=30, help='Frames per second (default: 30)')
    parser.add_argument('--pattern', choices=['sequential', 'zeros', 'random'], default='sequential', 
                       help='Data pattern (default: sequential)')
    parser.add_argument('--exit-frame', action='store_true', help='Write exit frame at end')
    
    args = parser.parse_args()
    
    print(f"Starting writer: buffer={args.buffer_name}, frames={args.num_frames}, fps={args.fps}", flush=True)
    
    # Create frame data based on pattern
    if args.pattern == 'sequential':
        # Create sequential pattern
        frame_data = bytearray(args.frame_size)
        for i in range(args.frame_size):
            frame_data[i] = i % 256
    elif args.pattern == 'zeros':
        frame_data = bytes(args.frame_size)
    else:  # random
        import random
        frame_data = bytes(random.randint(0, 255) for _ in range(args.frame_size))
    
    # Calculate hash for verification
    frame_hash = hashlib.sha256(frame_data).hexdigest()
    print(f"Frame pattern: {args.pattern}, size: {args.frame_size}, hash: {frame_hash}", flush=True)
    
    frame_interval = 1.0 / args.fps
    frames_written = 0
    
    try:
        # Connect to buffer
        with Writer(args.buffer_name) as writer:
            print("Connected to buffer", flush=True)
            
            # Write metadata
            metadata = f"TestWriter|pattern={args.pattern}|frames={args.num_frames}|hash={frame_hash}".encode()
            writer.set_metadata(metadata)
            print(f"Wrote metadata: {metadata.decode()}", flush=True)
            
            # Write frames
            start_time = time.time()
            next_frame_time = start_time
            
            for i in range(args.num_frames):
                # Write frame
                writer.write_frame(frame_data)
                frames_written += 1
                
                if (i + 1) % 100 == 0:
                    elapsed = time.time() - start_time
                    actual_fps = frames_written / elapsed if elapsed > 0 else 0
                    print(f"Progress: {i+1}/{args.num_frames} frames, {actual_fps:.1f} fps", flush=True)
                
                # Maintain frame rate
                next_frame_time += frame_interval
                sleep_time = next_frame_time - time.time()
                if sleep_time > 0:
                    time.sleep(sleep_time)
            
            # Write exit frame if requested
            if args.exit_frame:
                writer.write_frame(b"EXIT")
                print("Wrote EXIT frame", flush=True)
            
            # Final stats
            total_time = time.time() - start_time
            avg_fps = frames_written / total_time if total_time > 0 else 0
            print(f"Completed: {frames_written} frames in {total_time:.2f}s ({avg_fps:.1f} fps)", flush=True)
            
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()