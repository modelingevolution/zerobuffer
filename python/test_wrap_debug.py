#!/usr/bin/env python3
"""Debug script for buffer wrap-around test"""

import os
import sys
import time
import threading
import queue
import logging

# Setup detailed logging
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s.%(msecs)03d [%(threadName)s] %(levelname)s: %(message)s',
    datefmt='%H:%M:%S'
)

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from zerobuffer import Reader, Writer, BufferConfig

def test_buffer_wrap_around():
    """Test buffer wrap-around behavior with detailed logging"""
    
    buffer_name = f"test_wrap_debug_{os.getpid()}_{time.time()}"
    
    # Use small buffer to force wrap-around
    config = BufferConfig(metadata_size=256, payload_size=2048)
    
    print(f"\n=== Testing buffer wrap-around ===")
    print(f"Buffer name: {buffer_name}")
    print(f"Metadata size: {config.metadata_size}")
    print(f"Payload size: {config.payload_size}")
    print(f"Expected to wrap after a few frames\n")
    
    results = queue.Queue()
    frames_to_write = 20
    frame_size = 256  # Small frames
    
    def write_frames():
        """Writer thread"""
        try:
            with Writer(buffer_name) as writer:
                for i in range(frames_to_write):
                    data = bytes([i]) + b"x" * (frame_size - 1)
                    print(f"[Writer] Writing frame {i} (size={len(data)})")
                    writer.write_frame(data)
                    results.put(("wrote", i))
                    print(f"[Writer] Successfully wrote frame {i}")
                    time.sleep(0.01)  # Small delay
        except Exception as e:
            print(f"[Writer] ERROR: {e}")
            results.put(("writer_error", str(e)))
    
    def read_frames():
        """Reader thread"""
        try:
            with Reader(buffer_name, config) as reader:
                for i in range(frames_to_write):
                    print(f"[Reader] Attempting to read frame {i}")
                    frame = reader.read_frame(timeout=2.0)
                    if frame:
                        print(f"[Reader] Read frame {i}: seq={frame.sequence}, size={frame.size}, first_byte={frame.data[0]}")
                        assert frame.data[0] == i, f"Expected first byte {i}, got {frame.data[0]}"
                        assert len(frame.data) == frame_size, f"Expected size {frame_size}, got {len(frame.data)}"
                        reader.release_frame(frame)
                        results.put(("read", i))
                        print(f"[Reader] Successfully validated frame {i}")
                    else:
                        print(f"[Reader] TIMEOUT reading frame {i}")
                        results.put(("timeout", i))
                        break
        except Exception as e:
            print(f"[Reader] ERROR: {e}")
            import traceback
            traceback.print_exc()
            results.put(("reader_error", str(e)))
    
    # Start reader thread first
    print("Starting reader thread...")
    reader_thread = threading.Thread(target=read_frames, name="ReaderThread")
    reader_thread.start()
    
    # Give reader time to initialize
    time.sleep(0.5)
    
    # Start writer thread
    print("Starting writer thread...")
    writer_thread = threading.Thread(target=write_frames, name="WriterThread")
    writer_thread.start()
    
    # Wait for completion
    print("\nWaiting for threads to complete...")
    writer_thread.join(timeout=30)
    reader_thread.join(timeout=30)
    
    # Check results
    print("\n=== Results ===")
    writes = 0
    reads = 0
    errors = []
    timeouts = []
    
    while not results.empty():
        event_type, value = results.get()
        if event_type == "wrote":
            writes += 1
        elif event_type == "read":
            reads += 1
        elif event_type == "timeout":
            timeouts.append(value)
        elif event_type in ["writer_error", "reader_error"]:
            errors.append(f"{event_type}: {value}")
    
    print(f"Frames written: {writes}/{frames_to_write}")
    print(f"Frames read: {reads}/{frames_to_write}")
    
    if timeouts:
        print(f"Timeouts at frames: {timeouts}")
    
    if errors:
        print(f"Errors: {errors}")
    
    # Check assertions
    success = True
    if errors:
        print("\n❌ Test FAILED - errors occurred")
        success = False
    elif writes != frames_to_write:
        print(f"\n❌ Test FAILED - expected {frames_to_write} writes, got {writes}")
        success = False
    elif reads != frames_to_write:
        print(f"\n❌ Test FAILED - expected {frames_to_write} reads, got {reads}")
        success = False
    else:
        print("\n✅ Test PASSED - all frames transferred successfully with wrap-around")
    
    return success

if __name__ == "__main__":
    # Set environment variable for debug logging
    os.environ["ZEROBUFFER_LOG_LEVEL"] = "DEBUG"
    
    success = test_buffer_wrap_around()
    sys.exit(0 if success else 1)