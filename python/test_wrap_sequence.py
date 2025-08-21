#!/usr/bin/env python3
"""Test to verify sequence numbers are handled correctly during wrap-around"""

import os
import sys
import time

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from zerobuffer import Reader, Writer, BufferConfig

def test_wrap_sequence():
    """Test that sequence numbers remain consistent across wrap-around"""
    
    buffer_name = f"test_wrap_seq_{os.getpid()}_{time.time()}"
    
    # Use small buffer to force wrap-around
    config = BufferConfig(metadata_size=256, payload_size=2048)
    
    print(f"Testing sequence numbers during wrap-around")
    print(f"Buffer: {buffer_name}")
    print(f"Payload size: {config.payload_size} bytes")
    print()
    
    with Reader(buffer_name, config) as reader:
        with Writer(buffer_name) as writer:
            frame_size = 256
            
            # Write and read frames one by one to avoid buffer full
            # This will still cause wrap-around due to small buffer size
            print("Writing and reading frames with sequence validation...")
            
            for i in range(20):
                # Write frame
                data = bytes([i]) + b"x" * (frame_size - 1)
                writer.write_frame(data)
                expected_seq = i + 1  # Sequences start at 1
                print(f"Wrote frame {i}, sequence should be: {expected_seq}")
                
                # Read frame immediately
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None, f"Failed to read frame {i}"
                
                actual_seq = frame.sequence
                print(f"  Read frame {i}: seq={actual_seq}, first_byte={frame.data[0]}")
                
                # Verify sequence and data
                assert actual_seq == expected_seq, f"Sequence mismatch at frame {i}: expected {expected_seq}, got {actual_seq}"
                assert frame.data[0] == i, f"Data mismatch at frame {i}: expected first byte {i}, got {frame.data[0]}"
                
                reader.release_frame(frame)
                
                # Check if wrap-around happened (buffer is 2048 bytes, frame is 272 bytes with header)
                # After 7 frames we've used 1904 bytes, 8th frame would wrap
                if i == 7:
                    print("  *** Wrap-around should have occurred around here ***")
            
            print("\n✅ All sequences correct across wrap-around!")
            
            # Verify the counts
            print(f"\nWriter: frames_written={writer.frames_written}, sequence at {writer._sequence_number}")
            print(f"Reader: frames_read={reader.frames_read}, expecting sequence {reader._expected_sequence}")
            
            assert writer.frames_written == 20
            assert reader.frames_read == 20
            assert writer._sequence_number == 21  # Next sequence would be 21
            assert reader._expected_sequence == 21  # Expecting 21 next

if __name__ == "__main__":
    test_wrap_sequence()
    print("\n✅ Test completed successfully!")