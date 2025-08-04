"""
Advanced tests for ZeroBuffer - matching C# test suite

Tests that are missing from the basic test suite:
1. Free space accounting tests
2. Advanced resource cleanup tests  
3. Pattern validation with hash
4. Rapid write-read cycles
5. Metadata write-once validation
6. Metadata size validation
"""

import os
import hashlib
import time
import struct
import threading
import pytest
from typing import List

from zerobuffer import Reader, Writer, BufferConfig, Frame
from zerobuffer.exceptions import BufferFullException, MetadataAlreadyWrittenException, ZeroBufferException
from zerobuffer.types import OIEB, FrameHeader


class TestFreeSpaceAccounting:
    """Tests for verifying free space accounting works correctly"""
    
    def _get_free_space(self, reader: Reader) -> int:
        """Helper to get free space from OIEB"""
        oieb = reader._read_oieb()
        return oieb.payload_free_bytes
    
    def test_free_space_after_simple_wrap(self):
        """Test free space accounting after simple wrap"""
        buffer_name = f"test_freespace_wrap_{os.getpid()}"
        # Small buffer to force wrapping
        config = BufferConfig(metadata_size=100, payload_size=1000)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Get initial free space
                initial_free = self._get_free_space(reader)
                # Should be aligned to 1024 (block boundary)
                assert initial_free == 1024
                
                # Write frames until near the end
                frame_size = 100
                data = b"x" * frame_size
                
                # Write 8 frames (800 bytes + headers = ~928 bytes)
                for i in range(8):
                    writer.write_frame(data)
                
                # Read all frames to free up space
                for i in range(8):
                    frame = reader.read_frame()
                    assert len(frame.data) == frame_size
                    reader.release_frame(frame)
                
                after_read_free = self._get_free_space(reader)
                assert after_read_free == initial_free
                
                # Write a frame that will cause wrapping
                writer.write_frame(data)
                
                # Read the wrapped frame
                frame = reader.read_frame()
                assert len(frame.data) == frame_size
                reader.release_frame(frame)
                
                final_free = self._get_free_space(reader)
                assert final_free == initial_free
    
    def test_free_space_after_multiple_wraps(self):
        """Test free space accounting after multiple wraps"""
        buffer_name = f"test_freespace_multi_{os.getpid()}"
        # Very small buffer to force frequent wraps
        config = BufferConfig(metadata_size=100, payload_size=500)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                initial_free = self._get_free_space(reader)
                # Should be aligned to 512
                assert initial_free == 512
                
                # Do 10 wrap cycles
                for cycle in range(10):
                    # Write 3 frames that together will cause a wrap
                    data = b"y" * 120  # With header, ~136 bytes each
                    for i in range(3):
                        writer.write_frame(data)
                    
                    # Read all frames
                    for i in range(3):
                        frame = reader.read_frame()
                        assert len(frame.data) == 120
                        reader.release_frame(frame)
                    
                    current_free = self._get_free_space(reader)
                    assert current_free == initial_free
    
    def test_free_space_with_partial_reads(self):
        """Test free space with partial reads"""
        buffer_name = f"test_freespace_partial_{os.getpid()}"
        config = BufferConfig(metadata_size=100, payload_size=1000)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                initial_free = self._get_free_space(reader)
                
                # Write 5 frames
                data = b"z" * 100
                for i in range(5):
                    writer.write_frame(data)
                
                after_write_free = self._get_free_space(reader)
                expected_used = 5 * (FrameHeader.SIZE + 100)
                assert after_write_free == initial_free - expected_used
                
                # Read only 2 frames
                for i in range(2):
                    frame = reader.read_frame()
                    reader.release_frame(frame)
                
                after_partial_free = self._get_free_space(reader)
                expected_freed = 2 * (FrameHeader.SIZE + 100)
                assert after_partial_free == after_write_free + expected_freed
                
                # Read remaining 3 frames
                for i in range(3):
                    frame = reader.read_frame()
                    reader.release_frame(frame)
                
                final_free = self._get_free_space(reader)
                assert final_free == initial_free
    
    def test_wrap_marker_space_accounted_correctly(self):
        """Test wrap marker space accounting"""
        buffer_name = f"test_wrap_marker_{os.getpid()}"
        config = BufferConfig(metadata_size=100, payload_size=1000)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                frame_size = 100
                data = b"w" * frame_size
                
                # Write 9 frames to position near end
                for i in range(9):
                    writer.write_frame(data)
                    frame = reader.read_frame()
                    reader.release_frame(frame)
                
                before_wrap_free = self._get_free_space(reader)
                
                # Write one more frame that should cause wrap
                writer.write_frame(data)
                
                # Read the frame (which includes processing wrap marker)
                frame = reader.read_frame()
                assert len(frame.data) == frame_size
                reader.release_frame(frame)
                
                after_wrap_free = self._get_free_space(reader)
                assert after_wrap_free == before_wrap_free
    
    def test_stress_free_space_accounting(self):
        """Stress test free space accounting"""
        buffer_name = f"test_stress_free_{os.getpid()}"
        config = BufferConfig(metadata_size=100, payload_size=10000)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                initial_free = self._get_free_space(reader)
                total_written = 0
                total_read = 0
                
                # Do many random operations
                import random
                random.seed(42)
                
                for i in range(1000):
                    # Randomly decide to write or read
                    if random.random() < 0.5 and total_written - total_read < 50:
                        # Write a random sized frame
                        size = random.randint(10, 200)
                        data = bytes(size)
                        
                        try:
                            writer.write_frame(data)
                            total_written += 1
                        except BufferFullException:
                            # Expected when buffer is full
                            pass
                    elif total_read < total_written:
                        # Read a frame
                        frame = reader.read_frame(timeout=0.001)
                        if frame:
                            total_read += 1
                            reader.release_frame(frame)
                
                # Read all remaining frames
                while total_read < total_written:
                    frame = reader.read_frame(timeout=0.1)
                    if frame:
                        total_read += 1
                        reader.release_frame(frame)
                
                final_free = self._get_free_space(reader)
                assert final_free == initial_free


class TestAdvancedResourceCleanup:
    """Advanced resource cleanup tests"""
    
    def test_create_destroy_multiple_times(self):
        """Test create/destroy multiple times"""
        buffer_name = f"test_multi_create_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=10240)
        
        # Create and destroy 5 times
        for i in range(5):
            with Reader(buffer_name, config) as reader:
                assert reader.is_writer_connected() is False
                
                with Writer(buffer_name) as writer:
                    # Write and read a frame
                    data = bytes([i])
                    writer.write_frame(data)
                    
                    frame = reader.read_frame()
                    assert frame.data[0] == i
                    reader.release_frame(frame)
            
            # Small delay to ensure cleanup
            time.sleep(0.1)
    
    def test_multiple_buffers_cleanup(self):
        """Test multiple buffers cleanup"""
        buffer_names = [
            f"test_multi_buf_1_{os.getpid()}",
            f"test_multi_buf_2_{os.getpid()}",
            f"test_multi_buf_3_{os.getpid()}"
        ]
        
        readers = []
        writers = []
        
        try:
            # Create all buffers
            for name in buffer_names:
                reader = Reader(name, BufferConfig())
                readers.append(reader)
                
                writer = Writer(name)
                writers.append(writer)
                
                # Write something
                writer.write_frame(b"\x01\x02\x03")
            
            # Destroy them one by one
            for i in range(len(buffer_names)):
                writers[i].close()
                readers[i].close()
                
                time.sleep(0.1)
        finally:
            # Cleanup
            for writer in writers:
                if not writer._closed:
                    writer.close()
            for reader in readers:
                if not reader._closed:
                    reader.close()
    
    def test_cleanup_with_wrap_around(self):
        """Test cleanup with buffer wrap-around"""
        buffer_name = f"test_cleanup_wrap_{os.getpid()}"
        small_buffer = 4096  # Small buffer to force wrap
        config = BufferConfig(metadata_size=1024, payload_size=small_buffer)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write enough to wrap around
                frame_data = b"x" * 512
                for i in range(20):
                    frame_data = bytes([i]) + b"x" * 511
                    writer.write_frame(frame_data)
                    
                    frame = reader.read_frame()
                    assert len(frame.data) == 512
                    assert frame.data[0] == i
                    reader.release_frame(frame)
        
        # Verify reuse works
        time.sleep(0.1)
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                writer.write_frame(b"\xFF")
                frame = reader.read_frame()
                assert frame.data[0] == 0xFF
                reader.release_frame(frame)


class TestPatternValidation:
    """Pattern validation with hash tests"""
    
    def test_pattern_validation_with_hash(self):
        """Test pattern validation with hash verification"""
        buffer_name = f"test_pattern_hash_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=256*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Create pattern data
                def generate_pattern(frame: int, offset: int) -> int:
                    return (frame * 7 + offset * 13) % 256
                
                frame_count = 100
                frame_size = 1024
                
                # Store hashes for validation
                frame_hashes = {}
                
                # Write all frames first
                for i in range(frame_count):
                    data = bytearray(frame_size)
                    for j in range(frame_size):
                        data[j] = generate_pattern(i, j)
                    
                    # Calculate and store hash
                    frame_hashes[i] = hashlib.sha256(bytes(data)).hexdigest()
                    writer.write_frame(bytes(data))
                
                # Read and verify all frames
                received_frames = set()
                for i in range(frame_count):
                    frame = reader.read_frame(timeout=5.0)
                    assert frame is not None, f"Frame {i} is None"
                    
                    data = bytes(frame.data)
                    assert len(data) == frame_size
                    
                    # Verify the pattern matches expected for this frame
                    expected_first = generate_pattern(i, 0)
                    expected_last = generate_pattern(i, frame_size - 1)
                    assert data[0] == expected_first
                    assert data[frame_size - 1] == expected_last
                    
                    # Sample check in the middle
                    expected_middle = generate_pattern(i, frame_size // 2)
                    assert data[frame_size // 2] == expected_middle
                    
                    # Verify hash
                    actual_hash = hashlib.sha256(data).hexdigest()
                    assert actual_hash == frame_hashes[i]
                    
                    received_frames.add(i)
                    reader.release_frame(frame)
                
                assert len(received_frames) == frame_count


class TestRapidOperations:
    """Rapid operation tests"""
    
    def test_rapid_write_read_cycles(self):
        """Test rapid write-read cycles"""
        buffer_name = f"test_rapid_cycles_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                cycles = 1000
                start_time = time.time()
                
                for i in range(cycles):
                    data = struct.pack('I', i)
                    writer.write_frame(data)
                    
                    frame = reader.read_frame()
                    assert frame is not None
                    
                    value = struct.unpack('I', bytes(frame.data))[0]
                    assert value == i
                    reader.release_frame(frame)
                
                elapsed = time.time() - start_time
                throughput = cycles / elapsed
                
                # Should achieve at least 100 frames/second
                assert throughput >= 100
                print(f"Throughput: {throughput:.0f} frames/second")


class TestMetadataValidation:
    """Metadata validation tests"""
    
    def test_metadata_write_once(self):
        """Test metadata write-once constraint"""
        buffer_name = f"test_metadata_once_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write metadata
                metadata = b"Camera Config v1.0"
                writer.set_metadata(metadata)
                
                # Try to write metadata again - should fail
                with pytest.raises(MetadataAlreadyWrittenException):
                    writer.set_metadata(metadata)
                
                # Read metadata
                read_metadata = reader.get_metadata()
                assert bytes(read_metadata) == metadata
    
    def test_metadata_size_validation(self):
        """Test metadata size validation"""
        buffer_name = f"test_metadata_size_{os.getpid()}"
        config = BufferConfig(metadata_size=256, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Try to write metadata larger than buffer
                large_metadata = b"x" * 512
                
                with pytest.raises(ZeroBufferException):
                    writer.set_metadata(large_metadata)
                
                # Write metadata that fits
                small_metadata = b"y" * 128
                writer.set_metadata(small_metadata)
                
                read_metadata = reader.get_metadata()
                assert bytes(read_metadata) == small_metadata


class TestSequentialOperations:
    """Sequential operation tests"""
    
    def test_sequential_write_read(self):
        """Test sequential write/read within a single process"""
        buffer_name = f"test_sequential_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                frame_count = 100
                
                # Write frames
                for i in range(frame_count):
                    data = struct.pack('I', i)
                    writer.write_frame(data)
                
                # Read frames
                for i in range(frame_count):
                    frame = reader.read_frame(timeout=1.0)
                    assert frame is not None
                    
                    value = struct.unpack('I', bytes(frame.data))[0]
                    assert value == i
                    reader.release_frame(frame)
    
    def test_multiple_frames_sequential(self):
        """Test multiple frames sequential"""
        buffer_name = f"test_multi_seq_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=256*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                frame_count = 100
                
                # Write multiple frames
                for i in range(frame_count):
                    data = struct.pack('I', i)
                    writer.write_frame(data)
                
                # Read all frames
                for i in range(frame_count):
                    frame = reader.read_frame()
                    assert frame is not None
                    assert frame.sequence == i + 1
                    
                    value = struct.unpack('I', bytes(frame.data))[0]
                    assert value == i
                    reader.release_frame(frame)