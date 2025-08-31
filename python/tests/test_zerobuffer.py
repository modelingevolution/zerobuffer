"""
Unit tests for ZeroBuffer Python implementation
"""

import os
import time
import threading
import pytest

from zerobuffer import (
    Reader,
    Writer,
    BufferConfig,
    ZeroBufferException,
    WriterAlreadyConnectedException,
    FrameTooLargeException,
    MetadataAlreadyWrittenException,
)
from zerobuffer.types import FrameHeader, align_to_boundary


class TestTypes:
    """Test core data structures"""

    def test_oieb_view_direct_access(self) -> None:
        """Test OIEBView direct memory access"""
        from zerobuffer.oieb_view import OIEBView

        # Create a bytearray to simulate shared memory
        buffer = bytearray(128)

        # Create view
        oieb_view = OIEBView(memoryview(buffer))

        # Initialize with test values
        oieb_view.oieb_size = 128
        oieb_view.metadata_size = 1024
        oieb_view.metadata_free_bytes = 1024
        oieb_view.metadata_written_bytes = 0
        oieb_view.payload_size = 65536
        oieb_view.payload_free_bytes = 65536
        oieb_view.payload_write_pos = 0
        oieb_view.payload_read_pos = 0
        oieb_view.payload_written_count = 0
        oieb_view.payload_read_count = 0
        oieb_view.writer_pid = 1234
        oieb_view.reader_pid = 5678

        # Verify values are written directly to buffer
        assert oieb_view.oieb_size == 128
        assert oieb_view.metadata_size == 1024
        assert oieb_view.writer_pid == 1234
        assert oieb_view.reader_pid == 5678

        # Create another view on same buffer to verify data persistence
        oieb_view2 = OIEBView(memoryview(buffer))
        assert oieb_view2.oieb_size == 128
        assert oieb_view2.metadata_size == 1024
        assert oieb_view2.writer_pid == 1234
        assert oieb_view2.reader_pid == 5678

    def test_frame_header_pack_unpack(self) -> None:
        """Test FrameHeader serialization"""
        header = FrameHeader(payload_size=1024, sequence_number=42)

        data = header.pack()
        assert len(data) == 16  # FrameHeader is 16 bytes

        header2 = FrameHeader.unpack(data)
        assert header2.payload_size == header.payload_size
        assert header2.sequence_number == header.sequence_number

    def test_align_to_boundary(self) -> None:
        """Test alignment function"""
        assert align_to_boundary(0) == 0
        assert align_to_boundary(1) == 64
        assert align_to_boundary(63) == 64
        assert align_to_boundary(64) == 64
        assert align_to_boundary(65) == 128


class TestReaderWriter:
    """Test Reader and Writer basic functionality"""

    @pytest.fixture
    def buffer_name(self) -> str:
        """Generate unique buffer name for each test"""
        return f"test_buffer_{os.getpid()}_{time.time()}"

    def test_create_reader(self, buffer_name: str) -> None:
        """Test creating a reader"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            assert reader.name == buffer_name
            assert not reader.is_writer_connected()
            assert reader.frames_read == 0
            assert reader.bytes_read == 0

    def test_connect_writer(self, buffer_name: str) -> None:
        """Test connecting a writer to existing buffer"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            assert not reader.is_writer_connected()

            with Writer(buffer_name) as writer:
                assert writer.name == buffer_name
                assert reader.is_writer_connected()
                assert writer.is_reader_connected()
                assert writer.frames_written == 0
                assert writer.bytes_written == 0

    def test_multiple_writers_rejected(self, buffer_name: str) -> None:
        """Test that only one writer can connect"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config):
            with Writer(buffer_name):
                # Second writer should be rejected
                with pytest.raises(WriterAlreadyConnectedException):
                    Writer(buffer_name)

    def test_writer_without_reader(self, buffer_name: str) -> None:
        """Test that writer cannot connect without reader"""
        with pytest.raises(ZeroBufferException):
            Writer(buffer_name)

    def test_metadata_write_read(self, buffer_name: str) -> None:
        """Test metadata writing and reading"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        reader = Reader(buffer_name, config)
        try:
            # No metadata initially
            assert reader.get_metadata() is None

            writer = Writer(buffer_name)
            try:
                # Write metadata
                metadata = b"Test metadata content"
                writer.set_metadata(metadata)

                # Read metadata (zero-copy)
                read_metadata = reader.get_metadata()
                assert read_metadata is not None
                assert bytes(read_metadata) == metadata
                # Release the memoryview reference before closing
                del read_metadata

                # Cannot write metadata twice
                with pytest.raises(MetadataAlreadyWrittenException):
                    writer.set_metadata(b"Second metadata")
            finally:
                writer.close()
        finally:
            reader.close()


class TestFrameOperations:
    """Test frame reading and writing"""

    @pytest.fixture
    def buffer_name(self) -> str:
        """Generate unique buffer name for each test"""
        return f"test_buffer_{os.getpid()}_{time.time()}"

    def test_single_frame_write_read(self, buffer_name: str) -> None:
        """Test writing and reading a single frame"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write frame
                data = b"Hello, ZeroBuffer!"
                writer.write_frame(data)

                # Read frame
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                with frame:  # Use context manager for proper cleanup
                    assert frame.sequence == 1
                    assert frame.size == len(data)
                    assert bytes(frame.data) == data

                # Check stats
                assert writer.frames_written == 1
                assert writer.bytes_written == len(data)
                assert reader.frames_read == 1
                assert reader.bytes_read == len(data)

    def test_multiple_frames(self, buffer_name: str) -> None:
        """Test writing and reading multiple frames"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write multiple frames
                frames_to_write = 10
                for i in range(frames_to_write):
                    data = f"Frame {i}".encode()
                    writer.write_frame(data)

                # Read all frames
                for i in range(frames_to_write):
                    frame = reader.read_frame(timeout=1.0)
                    assert frame is not None
                    with frame:  # Use context manager for proper cleanup
                        assert frame.sequence == i + 1
                        expected_data = f"Frame {i}".encode()
                        assert bytes(frame.data) == expected_data

                assert writer.frames_written == frames_to_write
                assert reader.frames_read == frames_to_write

    def test_zero_copy_write(self, buffer_name: str) -> None:
        """Test zero-copy writing with memoryview"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Create a bytearray and get memoryview
                data = bytearray(b"Zero-copy data")
                view = memoryview(data)

                # Write with zero-copy (memoryview is zero-copy)
                writer.write_frame(view)

                # Read and verify
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                with frame:  # Use context manager for proper cleanup
                    assert bytes(frame.data) == data

    def test_frame_too_large(self, buffer_name: str) -> None:
        """Test writing frame larger than buffer"""
        config = BufferConfig(metadata_size=1024, payload_size=1024)  # Small buffer

        with Reader(buffer_name, config):
            with Writer(buffer_name) as writer:
                # Try to write frame larger than buffer
                large_data = b"x" * 2048
                with pytest.raises(FrameTooLargeException):
                    writer.write_frame(large_data)

    def test_buffer_wrap_around(self, buffer_name: str) -> None:
        """Test buffer wrap-around behavior"""
        import threading
        import queue
        from typing import Tuple, Union

        # Use small buffer to test wrap-around
        config = BufferConfig(metadata_size=256, payload_size=2048)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                frames_to_write = 20
                frame_size = 256  # Small frames
                results: queue.Queue[Tuple[str, Union[int, str]]] = queue.Queue()

                def write_frames() -> None:
                    """Writer thread"""
                    try:
                        for i in range(frames_to_write):
                            data = bytes([i]) + b"x" * (frame_size - 1)
                            writer.write_frame(data)
                            results.put(("wrote", i))
                    except Exception as e:
                        results.put(("error", str(e)))

                def read_frames() -> None:
                    """Reader thread"""
                    try:
                        for i in range(frames_to_write):
                            frame = reader.read_frame(timeout=2.0)
                            if frame:
                                with frame:  # Use context manager for proper cleanup
                                    assert frame.data[0] == i
                                    assert len(frame.data) == frame_size
                                    results.put(("read", i))
                            else:
                                results.put(("timeout", i))
                                break
                    except Exception as e:
                        results.put(("error", str(e)))

                # Start threads
                writer_thread = threading.Thread(target=write_frames)
                reader_thread = threading.Thread(target=read_frames)

                writer_thread.start()
                reader_thread.start()

                # Wait for completion
                writer_thread.join(timeout=10)
                reader_thread.join(timeout=10)

                # Check results
                writes = 0
                reads = 0
                errors = []
                while not results.empty():
                    event_type, value = results.get()
                    if event_type == "wrote":
                        writes += 1
                    elif event_type == "read":
                        reads += 1
                    elif event_type == "error":
                        errors.append(value)

                assert not errors, f"Errors occurred: {errors}"
                assert writes == frames_to_write, f"Expected {frames_to_write} writes, got {writes}"
                assert reads == frames_to_write, f"Expected {frames_to_write} reads, got {reads}"


class TestErrorConditions:
    """Test error handling and edge cases"""

    @pytest.fixture
    def buffer_name(self) -> str:
        """Generate unique buffer name for each test"""
        return f"test_buffer_{os.getpid()}_{time.time()}"

    def test_reader_timeout(self, buffer_name: str) -> None:
        """Test reader timeout when no data available"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name):
                # Try to read with short timeout
                frame = reader.read_frame(timeout=0.1)
                assert frame is None  # Should timeout

    def test_sequence_validation(self, buffer_name: str) -> None:
        """Test sequence number validation"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write frames
                writer.write_frame(b"Frame 1")
                writer.write_frame(b"Frame 2")

                # Read first frame
                frame1 = reader.read_frame(timeout=1.0)
                assert frame1 is not None
                with frame1:  # Use context manager for proper cleanup
                    assert frame1.sequence == 1

                # Manually corrupt sequence in next frame
                # This would require direct memory access - skip for now
                # Just verify normal sequence works
                frame2 = reader.read_frame(timeout=1.0)
                assert frame2 is not None
                with frame2:  # Use context manager for proper cleanup
                    assert frame2.sequence == 2

    def test_empty_frame_rejected(self, buffer_name: str) -> None:
        """Test that empty frames are rejected"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config):
            with Writer(buffer_name) as writer:
                with pytest.raises(Exception):  # Should raise InvalidFrameSizeException
                    writer.write_frame(b"")


class TestConcurrency:
    """Test concurrent operations"""

    @pytest.fixture
    def buffer_name(self) -> str:
        """Generate unique buffer name for each test"""
        return f"test_buffer_{os.getpid()}_{time.time()}"

    def test_concurrent_write_read(self, buffer_name: str) -> None:
        """Test concurrent writing and reading"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        frames_to_transfer = 100

        def writer_thread(name: str) -> None:
            with Writer(name) as writer:
                for i in range(frames_to_transfer):
                    data = f"Frame {i}".encode()
                    writer.write_frame(data)
                    time.sleep(0.001)  # Small delay

        def reader_thread(name: str, results: list[list[int]]) -> None:
            with Reader(name, config) as reader:
                frames_read = []
                for i in range(frames_to_transfer):
                    frame = reader.read_frame(timeout=5.0)
                    if frame:
                        with frame:  # Use context manager for proper cleanup
                            frames_read.append(frame.sequence)
                results.append(frames_read)

        # Start reader first
        results: list[list[int]] = []
        reader_t = threading.Thread(target=reader_thread, args=(buffer_name, results))
        reader_t.start()

        # Give reader time to initialize
        time.sleep(0.1)

        # Start writer
        writer_t = threading.Thread(target=writer_thread, args=(buffer_name,))
        writer_t.start()

        # Wait for completion
        writer_t.join(timeout=10.0)
        reader_t.join(timeout=10.0)

        # Verify all frames were transferred
        assert len(results) == 1
        assert len(results[0]) == frames_to_transfer
        assert results[0] == list(range(1, frames_to_transfer + 1))


class TestZeroCopyVerification:
    """Verify zero-copy behavior"""

    @pytest.fixture
    def buffer_name(self) -> str:
        """Generate unique buffer name for each test"""
        return f"test_buffer_{os.getpid()}_{time.time()}"

    def test_memoryview_zero_copy(self, buffer_name: str) -> None:
        """Test that memoryview provides zero-copy access"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Write data
                original_data = bytearray(b"Original data that should not be copied")
                writer.write_frame(original_data)

                # Read frame
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None

                with frame:  # Use context manager for proper cleanup
                    # Get memoryview of frame data
                    frame_view = frame.data
                    assert isinstance(frame_view, memoryview)

                    # Verify data matches without copying
                    assert frame_view.tobytes() == original_data

                    # Test that we can slice without copying
                    slice_view = frame_view[0:8]
                    assert isinstance(slice_view, memoryview)
                    assert slice_view.tobytes() == original_data[0:8]

                    # Clean up local references
                    del slice_view
                    del frame_view

    def test_direct_buffer_access(self, buffer_name: str) -> None:
        """Test direct buffer access API"""
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        with Reader(buffer_name, config) as reader:
            with Writer(buffer_name) as writer:
                # Get direct buffer for writing
                data_to_write = b"Direct buffer access"
                buffer = writer.get_frame_buffer(len(data_to_write))

                # Write directly into buffer
                buffer[:] = data_to_write

                # Commit the frame
                writer.commit_frame()

                # Release the memoryview reference
                del buffer

                # Read and verify
                frame = reader.read_frame(timeout=1.0)
                assert frame is not None
                with frame:  # Use context manager for proper cleanup
                    assert bytes(frame.data) == data_to_write


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
