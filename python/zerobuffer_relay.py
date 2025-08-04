#!/usr/bin/env python3
"""
ZeroBuffer Relay Helper - reads from one buffer and writes to another

Usage: python zerobuffer_relay.py <input_buffer> <output_buffer>
"""

import sys
import time
import signal
from zerobuffer import Reader, Writer, BufferConfig


def signal_handler(sig, frame):
    sys.exit(0)


def main():
    if len(sys.argv) != 3:
        print("Usage: zerobuffer_relay.py <input_buffer> <output_buffer>")
        sys.exit(1)
    
    input_buffer = sys.argv[1]
    output_buffer = sys.argv[2]
    
    # Register signal handler for clean shutdown
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    # Create large buffer for performance testing
    config = BufferConfig(metadata_size=4096, payload_size=256 * 1024 * 1024)  # 256MB
    
    try:
        # Create input buffer as reader
        with Reader(input_buffer, config) as reader:
            # Wait for benchmark to create output buffer
            writer = None
            for retry in range(50):  # 5 second timeout
                try:
                    writer = Writer(output_buffer)
                    break
                except:
                    time.sleep(0.1)
            
            if writer is None:
                print(f"Failed to connect to output buffer: {output_buffer}", file=sys.stderr)
                sys.exit(1)
            
            with writer:
                    print(f"Relay started: {input_buffer} -> {output_buffer}", flush=True)
                    
                    # Relay frames
                    while True:
                        frame = reader.read_frame(timeout=0.1)
                        if frame:
                            # Relay the frame data as-is (including timestamp)
                            writer.write_frame(bytes(frame.data))
                            reader.release_frame(frame)
                        
    except KeyboardInterrupt:
        pass
    except Exception as e:
        print(f"Relay error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()