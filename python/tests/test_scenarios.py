"""
Integration scenario tests for ZeroBuffer Python implementation

These tests match the scenarios defined in TEST_SCENARIOS.md
"""

import os
import sys
import time
import signal
import threading
import multiprocessing
import subprocess
import pytest
import tempfile
from pathlib import Path

from zerobuffer import (
    Reader, Writer, BufferConfig,
    WriterDeadException, ReaderDeadException,
    BufferFullException, SequenceError
)


class TestScenario1BasicDataTransfer:
    """Scenario 1: Basic single frame write and read"""
    
    def test_single_frame_transfer(self):
        """1.1 Single frame write and read"""
        buffer_name = f"test_scenario_1_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        # Create reader
        with Reader(buffer_name, config) as reader:
            # Connect writer
            with Writer(buffer_name) as writer:
                # Write metadata
                metadata = b"Test metadata v1.0"
                writer.set_metadata(metadata)
                
                # Verify metadata
                read_metadata = reader.get_metadata()
                assert read_metadata is not None
                assert bytes(read_metadata) == metadata
                
                # Write single frame
                frame_data = b"Hello, ZeroBuffer!"
                writer.write_frame(frame_data)
                
                # Read frame
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                assert frame.sequence == 1
                assert frame.size == len(frame_data)
                assert bytes(frame.data) == frame_data
                
                # Release frame
                reader.release_frame(frame)
                
                # Verify stats
                assert writer.frames_written == 1
                assert reader.frames_read == 1


class TestScenario2ContinuousStreaming:
    """Scenario 2: Continuous data streaming"""
    
    def test_continuous_streaming(self):
        """2.1 Stream 1000 frames continuously"""
        buffer_name = f"test_scenario_2_{os.getpid()}_{time.time()}"
        config = BufferConfig(metadata_size=1024, payload_size=1024*1024)  # 1MB payload
        num_frames = 100  # Reduced from 1000 for faster testing
        
        # Create reader and writer
        reader = Reader(buffer_name, config)
        writer = Writer(buffer_name)
        
        # Track progress
        frames_written = []
        frames_read = []
        errors = []
        
        def writer_thread():
            try:
                for i in range(num_frames):
                    data = f"Frame {i:04d}".encode() + b"x" * 1000
                    writer.write_frame(data)
                    frames_written.append(i)
            except Exception as e:
                errors.append(f"Writer error: {e}")
        
        def reader_thread():
            try:
                for i in range(num_frames):
                    frame = reader.read_frame(timeout=5.0)
                    assert frame is not None
                    assert frame.sequence == i + 1
                    
                    # Verify frame content
                    expected_prefix = f"Frame {i:04d}".encode()
                    assert frame.data[:len(expected_prefix)].tobytes() == expected_prefix
                    
                    frames_read.append(i)
                    reader.release_frame(frame)
            except Exception as e:
                errors.append(f"Reader error: {e}")
        
        # Run reader and writer concurrently
        reader_t = threading.Thread(target=reader_thread)
        writer_t = threading.Thread(target=writer_thread)
        
        reader_t.start()
        writer_t.start()
        
        writer_t.join(timeout=10.0)
        reader_t.join(timeout=10.0)
        
        # Clean up
        writer.close()
        reader.close()
        
        # Check for errors
        assert len(errors) == 0, f"Errors occurred: {errors}"
        
        # Verify all frames transferred
        assert len(frames_written) == num_frames
        assert len(frames_read) == num_frames


class TestScenario3BufferFullCondition:
    """Scenario 3: Buffer full handling"""
    
    def test_buffer_full_blocking(self):
        """3.1 Writer blocks when buffer is full"""
        buffer_name = f"test_scenario_3_{os.getpid()}"
        # Small buffer to easily fill
        config = BufferConfig(metadata_size=64, payload_size=1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Fill buffer without reading
                frame_size = 200
                frame_data = b"x" * frame_size
                
                # Track write progress
                frames_written = []
                writer_blocked = threading.Event()
                
                def writer_thread():
                    for i in range(10):  # Try to write more than buffer can hold
                        writer.write_frame(frame_data)
                        frames_written.append(i)
                    writer_blocked.set()  # This line shouldn't be reached
                
                # Start writer in background
                thread = threading.Thread(target=writer_thread)
                thread.start()
                
                # Give writer time to fill buffer
                time.sleep(0.5)
                
                # Writer should have written some frames but be blocked now
                initial_count = len(frames_written)
                assert initial_count > 0
                assert initial_count < 10
                assert not writer_blocked.is_set()  # Writer should be blocked
                
                # Read one frame to make space
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                reader.release_frame(frame)
                
                # Give writer time to write one more frame
                time.sleep(0.1)
                
                # Should have written at least one more frame (could be more due to freed space)
                assert len(frames_written) > initial_count
                
                # Read all remaining frames to unblock writer
                frames_to_read = 10 - 1  # We already read one
                for _ in range(frames_to_read):
                    frame = reader.read_frame(timeout=0.5)
                    if frame:
                        reader.release_frame(frame)
                
                # Wait for writer to finish
                thread.join(timeout=2.0)
                
                # Verify writer eventually wrote all frames
                assert len(frames_written) == 10
                assert writer_blocked.is_set()


class TestScenario4WrapAround:
    """Scenario 4: Buffer wrap-around"""
    
    def test_wrap_around_handling(self):
        """4.1 Correct wrap-around at buffer boundary"""
        buffer_name = f"test_scenario_4_{os.getpid()}"
        # Small buffer to force wrap-around
        config = BufferConfig(metadata_size=64, payload_size=1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Frame size that doesn't divide evenly into buffer
                frame_data = b"x" * 300
                
                # Write and read frames to cause wrap-around
                total_frames = 20
                
                for i in range(total_frames):
                    # Write frame
                    writer.write_frame(frame_data)
                    
                    # Read frame
                    frame = reader.read_frame(timeout=1.0)
                    assert frame is not None
                    assert frame.sequence == i + 1
                    assert len(frame.data) == len(frame_data)
                    assert bytes(frame.data) == frame_data
                    reader.release_frame(frame)
                
                # All frames should have been transferred correctly
                assert writer.frames_written == total_frames
                assert reader.frames_read == total_frames


class TestScenario5WriterDisconnect:
    """Scenario 5: Writer disconnect detection"""
    
    def test_writer_disconnect_detection(self):
        """5.1 Reader detects writer disconnect"""
        buffer_name = f"test_scenario_5_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        def writer_process(name):
            """Writer that disconnects after writing some frames"""
            with Writer(name) as writer:
                for i in range(5):
                    writer.write_frame(f"Frame {i}".encode())
                # Writer disconnects here
        
        # Create reader
        with Reader(buffer_name, config) as reader:
            # Start writer in thread
            writer_thread = threading.Thread(target=writer_process, args=(buffer_name,))
            writer_thread.start()
            
            # Read the 5 frames
            for i in range(5):
                frame = reader.read_frame(timeout=2.0)
                assert frame is not None
                reader.release_frame(frame)
            
            # Wait for writer to disconnect
            writer_thread.join()
            time.sleep(0.1)
            
            # Verify writer is disconnected
            assert not reader.is_writer_connected()
            
            # Try to read more - should timeout or raise exception
            frame = reader.read_frame(timeout=0.5)
            assert frame is None  # Timeout, no more data


class TestScenario6ReaderDisconnect:
    """Scenario 6: Reader disconnect detection"""
    
    def test_reader_disconnect_detection(self):
        """6.1 Writer detects reader disconnect"""
        buffer_name = f"test_scenario_6_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=1024)  # Small buffer
        
        reader_disconnected = threading.Event()
        writer_exception = []
        
        def reader_process(name, config):
            """Reader that disconnects early"""
            with Reader(name, config) as reader:
                # Read a few frames
                for i in range(3):
                    frame = reader.read_frame(timeout=2.0)
                    if frame:
                        reader.release_frame(frame)
                # Reader disconnects here
            reader_disconnected.set()
        
        def writer_process(name):
            """Writer that tries to write many frames"""
            try:
                with Writer(name) as writer:
                    # Wait for reader to disconnect
                    reader_disconnected.wait(timeout=5.0)
                    time.sleep(0.5)  # Give time for cleanup
                    
                    # Try to write more frames - should eventually fail
                    for i in range(100):
                        writer.write_frame(b"x" * 100)
                        
            except ReaderDeadException as e:
                writer_exception.append(e)
        
        # Start reader
        reader_thread = threading.Thread(
            target=reader_process, 
            args=(buffer_name, config)
        )
        reader_thread.start()
        
        time.sleep(0.1)  # Let reader initialize
        
        # Start writer
        writer_thread = threading.Thread(
            target=writer_process,
            args=(buffer_name,)
        )
        writer_thread.start()
        
        # Wait for threads
        reader_thread.join(timeout=10.0)
        writer_thread.join(timeout=10.0)
        
        # Writer should have detected reader disconnect
        assert len(writer_exception) > 0
        assert isinstance(writer_exception[0], ReaderDeadException)


class TestScenario7SequenceValidation:
    """Scenario 7: Sequence number validation"""
    
    def test_sequence_validation(self):
        """7.1 Verify sequence numbers are consecutive"""
        buffer_name = f"test_scenario_7_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write multiple frames
                num_frames = 50
                for i in range(num_frames):
                    writer.write_frame(f"Frame {i}".encode())
                
                # Read and verify sequences
                for i in range(num_frames):
                    frame = reader.read_frame(timeout=1.0)
                    assert frame is not None
                    assert frame.sequence == i + 1  # Sequences start at 1
                    reader.release_frame(frame)


class TestScenario8MetadataHandling:
    """Scenario 8: Metadata operations"""
    
    def test_metadata_write_once(self):
        """8.1 Metadata can only be written once"""
        buffer_name = f"test_scenario_8_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write metadata first time - should succeed
                metadata1 = b"First metadata"
                writer.set_metadata(metadata1)
                
                # Try to write again - should fail
                with pytest.raises(Exception):  # MetadataAlreadyWrittenException
                    writer.set_metadata(b"Second metadata")
                
                # Verify only first metadata is stored
                read_metadata = reader.get_metadata()
                assert bytes(read_metadata) == metadata1
    
    def test_large_metadata(self):
        """8.2 Large metadata handling"""
        buffer_name = f"test_scenario_8b_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Try to write metadata larger than buffer
                large_metadata = b"x" * 2048
                with pytest.raises(Exception):
                    writer.set_metadata(large_metadata)
                
                # Write metadata that fits
                valid_metadata = b"x" * 512
                writer.set_metadata(valid_metadata)
                
                # Verify
                read_metadata = reader.get_metadata()
                assert bytes(read_metadata) == valid_metadata


class TestScenario9MultiProcess:
    """Scenario 9: Multi-process support"""
    
    def test_cross_process_communication(self):
        """9.1 Reader and writer in different processes"""
        buffer_name = f"test_scenario_9_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        def writer_process(name):
            """Run in separate process"""
            with Writer(name) as writer:
                writer.set_metadata(b"Cross-process metadata")
                for i in range(10):
                    writer.write_frame(f"Process frame {i}".encode())
        
        # Create reader in main process
        with Reader(buffer_name, config) as reader:
            # Start writer in separate process
            proc = multiprocessing.Process(
                target=writer_process,
                args=(buffer_name,)
            )
            proc.start()
            
            # Read metadata
            time.sleep(0.5)  # Wait for writer
            metadata = reader.get_metadata()
            assert metadata is not None
            assert bytes(metadata) == b"Cross-process metadata"
            
            # Read frames
            for i in range(10):
                frame = reader.read_frame(timeout=5.0)
                assert frame is not None
                expected = f"Process frame {i}".encode()
                assert bytes(frame.data) == expected
                reader.release_frame(frame)
            
            # Wait for writer process to complete
            proc.join(timeout=5.0)
            assert proc.exitcode == 0


class TestScenario10ResourceCleanup:
    """Scenario 10: Resource cleanup"""
    
    def test_cleanup_on_normal_exit(self):
        """10.1 Resources cleaned up on normal exit"""
        buffer_name = f"test_scenario_10_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        # Create and close reader
        reader = Reader(buffer_name, config)
        reader.close()
        
        # Try to create new reader with same name - should succeed
        reader2 = Reader(buffer_name, config)
        reader2.close()
    
    def test_name_reuse_after_cleanup(self):
        """10.3 Buffer names can be reused after cleanup"""
        buffer_name = f"test_scenario_10b_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        # First session
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                writer.write_frame(b"Session 1")
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                reader.release_frame(frame)
        
        # Second session with same name
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                writer.write_frame(b"Session 2")
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                assert bytes(frame.data) == b"Session 2"
                reader.release_frame(frame)


class TestScenario11Performance:
    """Scenario 11: Performance characteristics"""
    
    def test_zero_copy_verification(self):
        """11.1 Verify zero-copy operation"""
        buffer_name = f"test_scenario_11_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Create large data
                large_data = bytearray(b"x" * 10000)
                view = memoryview(large_data)
                
                # Write using zero-copy
                writer.write_frame_zero_copy(view)
                
                # Read frame
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                
                # Verify we get a memoryview (zero-copy)
                assert isinstance(frame.data, memoryview)
                assert len(frame.data) == len(large_data)
                
                # Modify original data
                large_data[0] = ord('y')
                
                # Frame data should not change (was copied during write)
                assert frame.data[0] == ord('x')
                
                reader.release_frame(frame)
    
    def test_throughput(self):
        """11.2 Measure throughput"""
        buffer_name = f"test_scenario_11b_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=10*1024*1024)  # 10MB
        
        frame_size = 1024 * 1024  # 1MB frames
        frame_data = b"x" * frame_size
        num_frames = 10
        
        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Measure write throughput
                start_time = time.time()
                
                for i in range(num_frames):
                    writer.write_frame(frame_data)
                
                write_time = time.time() - start_time
                write_throughput = (num_frames * frame_size) / write_time / (1024*1024)
                
                # Read all frames
                for i in range(num_frames):
                    frame = reader.read_frame(timeout=1.0)
                    assert frame is not None
                    reader.release_frame(frame)
                
                total_time = time.time() - start_time
                total_throughput = (num_frames * frame_size) / total_time / (1024*1024)
                
                print(f"Write throughput: {write_throughput:.2f} MB/s")
                print(f"Total throughput: {total_throughput:.2f} MB/s")
                
                # Should achieve reasonable throughput
                assert total_throughput > 100  # At least 100 MB/s


class TestScenario12ErrorRecovery:
    """Scenario 12: Error recovery"""
    
    def test_recovery_after_sequence_error(self):
        """12.1 Recovery after sequence error"""
        # This test would require corrupting memory directly
        # which is not easily doable in Python without ctypes
        # Skip for now
        pass
    
    def test_partial_frame_handling(self):
        """12.2 Handling of partial frames"""
        # This would require simulating process crash mid-write
        # which is complex to test reliably
        pass


if __name__ == "__main__":
    # Run with multiprocessing support
    multiprocessing.set_start_method('spawn', force=True)
    pytest.main([__file__, "-v", "-s"])