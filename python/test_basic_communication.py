#!/usr/bin/env python3
"""
Test basic communication between reader and writer in same process
"""

import asyncio
import time
from zerobuffer import Reader, Writer, BufferConfig


async def test_basic_communication():
    """Test basic reader/writer communication"""
    print("Testing basic ZeroBuffer communication...")
    
    # Create buffer
    buffer_name = "test-basic-comm"
    config = BufferConfig(metadata_size=1024, payload_size=10240)
    
    print(f"1. Creating buffer '{buffer_name}'...")
    reader = Reader(buffer_name, config)
    print("   ✓ Buffer created")
    
    # Connect writer
    print(f"2. Connecting writer to buffer...")
    writer = Writer(buffer_name)
    print("   ✓ Writer connected")
    
    # Write metadata
    print("3. Writing metadata...")
    metadata = b"version=1.0"
    writer.set_metadata(metadata)
    print("   ✓ Metadata written")
    
    # Write a frame
    print("4. Writing frame...")
    frame_data = b"Hello, ZeroBuffer!"
    writer.write_frame(frame_data)
    print(f"   ✓ Frame written (size={len(frame_data)})")
    
    # Read the frame
    print("5. Reading frame...")
    frame = reader.read_frame()
    if frame:
        print(f"   ✓ Frame read: sequence={frame.sequence}, size={frame.size}")
        print(f"   ✓ Data: {bytes(frame.data)}")
        
        # Release frame to signal space available
        reader.release_frame(frame)
        print("   ✓ Released frame (signaled space available)")
    else:
        print("   ✗ No frame available")
        
    # Clean up
    writer.close()
    reader.close()
    print("\n✅ Test completed successfully!")


if __name__ == "__main__":
    asyncio.run(test_basic_communication())