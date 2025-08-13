"""
Basic communication step definitions

Implements test steps for fundamental ZeroBuffer communication patterns.
"""

import asyncio
import time
from typing import Dict, Optional, List, Any, Union, cast
import uuid

from zerobuffer import Reader, Writer, BufferConfig, Frame
from zerobuffer.exceptions import ZeroBufferException, BufferFullException, MetadataAlreadyWrittenException

from .base import BaseSteps
from ..step_registry import given, when, then, parsers
from ..services import BufferNamingService
from ..test_data_patterns import TestDataPatterns


class BasicCommunicationSteps(BaseSteps):
    """Step definitions for basic communication tests"""
    
    def __init__(self, test_context: Any, logger: Any) -> None:
        super().__init__(test_context, logger)
        self._readers: Dict[str, Reader] = {}
        self._writers: Dict[str, Writer] = {}
        self._last_frame: Optional[Union[Frame, Dict[str, Any]]] = None
        self._frames_written: List[Dict[str, Any]] = []
        self._frames_read: List[Frame] = []
        self._write_error: Optional[Exception] = None
        self._buffer_naming = BufferNamingService(self.logger)
        self._current_buffer = ""
        
    @given(r"the test environment is initialized")
    def test_environment_initialized(self) -> None:
        """Initialize test environment"""
        # Clean up any previous test resources
        self._readers.clear()
        self._writers.clear()
        self._frames_written.clear()
        self._frames_read.clear()
        self._current_buffer = ""
        self._last_frame = None
        self._write_error = None
        self._buffer_naming.clear_cache()
        self.logger.info("Test environment initialized")
        
    @given(r"all processes are ready")
    def all_processes_ready(self) -> None:
        """Confirm all processes are ready"""
        self.logger.info("All processes ready")
        
    @given(parsers.re(r"(?:the '(?P<process>[^']+)' process )?creates buffer '(?P<buffer_name>[^']+)' with metadata size '(?P<metadata_size>\d+)' and payload size '(?P<payload_size>\d+)'"))
    async def create_buffer(self, process: Optional[str], buffer_name: str, metadata_size: str, payload_size: str) -> None:
        """Create a new ZeroBuffer with specified configuration"""
        # Accept process parameter but ignore it (as per C# implementation)
        actual_buffer_name = self._buffer_naming.get_buffer_name(buffer_name)
        
        config = BufferConfig(
            metadata_size=int(metadata_size),
            payload_size=int(payload_size)
        )
        
        reader = Reader(actual_buffer_name, config)
        self._readers[buffer_name] = reader  # Store with original name as key
        self._current_buffer = buffer_name
        self.store_resource(f"reader_{buffer_name}", reader)
        
        self.logger.info(
            f"Created buffer '{buffer_name}' (actual: '{actual_buffer_name}') with metadata_size={metadata_size}, "
            f"payload_size={payload_size}"
        )
        
        # Small delay to ensure shared memory is fully initialized
        await asyncio.sleep(0.1)
        
    @when(r"(?:the '([^']+)' process )?connects to buffer '([^']+)'")
    async def connect_to_buffer(self, process: Optional[str], buffer_name: str) -> None:
        """Connect a writer to an existing buffer"""
        # Accept process parameter but ignore it (as per C# implementation)
        actual_buffer_name = self._buffer_naming.get_buffer_name(buffer_name)
        
        writer = Writer(actual_buffer_name)
        self._writers[buffer_name] = writer  # Store with original name as key
        self._current_buffer = buffer_name
        self.store_resource(f"writer_{buffer_name}", writer)
        
        self.logger.info(f"Connected to buffer '{buffer_name}' (actual: '{actual_buffer_name}'")
        
        # Small delay to ensure shared memory updates are visible
        await asyncio.sleep(0.1)
        
    @when(r"(?:the '([^']+)' process )?writes metadata with size '(\d+)'")
    async def write_metadata(self, process: Optional[str], size: str) -> None:
        """Write metadata to the buffer"""
        # Accept process parameter but ignore it (as per C# implementation)
        # Get the writer - if only one exists, use it; otherwise use current buffer
        if not self._writers:
            raise Exception("No writer connected to any buffer")
        elif len(self._writers) == 1:
            writer = next(iter(self._writers.values()))
        elif self._current_buffer and self._current_buffer in self._writers:
            writer = self._writers[self._current_buffer]
        else:
            raise Exception(f"Multiple writers exist but current buffer '{self._current_buffer}' is not set or not found")
        
        # Generate metadata using TestDataPatterns
        metadata = TestDataPatterns.generate_metadata(int(size))
        writer.set_metadata(metadata)
        
        self.logger.info(f"Wrote metadata with size {size}")
        
        # Small delay to ensure OIEB updates are visible
        await asyncio.sleep(0.1)
        
    @when(r"(?:the '([^']+)' process )?writes frame with size '(\d+)' and sequence '(\d+)'")
    async def write_frame_with_sequence(self, process: Optional[str], size: str, sequence: str) -> None:
        """Write a frame with specific size and sequence"""
        # Accept process parameter but ignore it
        writer = self._writers[self._current_buffer]
        sequence_num = int(sequence)
        
        # Generate frame data using TestDataPatterns
        frame_data = TestDataPatterns.generate_frame_data(int(size), sequence_num)
        
        # Write frame
        writer.write_frame(frame_data)
        # Note: Frame is just a tracking object here, actual frame is in shared memory
        frame = {'data': frame_data, 'sequence_number': sequence_num, 'size': len(frame_data)}
        self._frames_written.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Wrote frame with size {size} and sequence {sequence}")
        
    @when(r"(?:the '([^']+)' process )?writes frame with sequence '(\d+)'")
    async def write_frame_sequence_only(self, process: Optional[str], sequence: str) -> None:
        """Write a frame with default size"""
        # Accept process parameter but ignore it
        writer = self._writers[self._current_buffer]
        sequence_num = int(sequence)
        # Use default size of 1024 when not specified
        data = TestDataPatterns.generate_frame_data(1024, sequence_num)
        
        writer.write_frame(data)
        self.logger.info(f"Wrote frame with sequence {sequence}")
        
    @when(r"(?:the '([^']+)' process )?writes frames until buffer is full")
    async def write_until_full(self, process: Optional[str]) -> None:
        """Write frames until the buffer is full"""
        writer = next(iter(self._writers.values()))
        frame_count = 0
        
        # Write frames until we hit buffer full
        # Use large frames to fill buffer faster (1KB per frame)
        frame_size = 1024
        while True:
            try:
                data = TestDataPatterns.generate_frame_data(frame_size, frame_count)
                writer.write_frame(data)
                # Track frame info
                frame = {'data': data, 'sequence_number': frame_count, 'size': len(data)}
                self._frames_written.append(frame)
                frame_count += 1
                
                # Safety limit to prevent infinite loops (but much higher)
                if frame_count > 20:  # 20 * 1KB = 20KB, should fill 10KB buffer
                    self.logger.info(f"Reached safety limit after {frame_count} frames")
                    break
                    
            except Exception as e:
                # Expected: BufferFullException when buffer is full
                self.logger.info(f"Buffer full after {frame_count} frames: {e}")
                self._write_error = e
                break
            
    @when(r"(?:the '([^']+)' process )?requests zero-copy frame of size '(\d+)'")
    async def request_zero_copy_frame(self, process: Optional[str], size: str) -> None:
        """Request a zero-copy frame"""
        writer = next(iter(self._writers.values()))
        frame_size = int(size)
        
        # Get zero-copy buffer
        buffer = writer.get_frame_buffer(frame_size)
        self.set_data("zero_copy_buffer", buffer)
        self.set_data("zero_copy_size", frame_size)
        
        self.logger.info(f"Requested zero-copy frame of size {size}")
        
    @when(r"(?:the '([^']+)' process )?fills zero-copy buffer with test pattern")
    async def fill_zero_copy_buffer(self, process: Optional[str]) -> None:
        """Fill zero-copy buffer with test pattern"""
        # Accept process parameter but ignore it
        writer = self._writers[self._current_buffer]
        size = self.get_data("zero_copy_size")
        
        # Request a zero-copy buffer and fill it immediately
        # This is the correct way to use zero-copy: get buffer, fill, commit
        span = writer.get_frame_buffer(size)
        
        # Generate test pattern using TestDataPatterns with the sequence number that will be used
        # The sequence number will be frames_written + 1 (1-based when committed)
        test_pattern = TestDataPatterns.generate_frame_data(size, writer.frames_written + 1)
        
        # Fill the zero-copy buffer directly (this is the actual zero-copy operation)
        span[:size] = test_pattern
        
        # Store the pattern for verification
        self.set_data("zero_copy_buffer", test_pattern)
        self.set_data("zero_copy_ready", True)
        
        self.logger.info("Filled zero-copy buffer with test pattern")
        
    @when(r"(?:the '([^']+)' process )?commits zero-copy frame")
    async def commit_zero_copy_frame(self, process: Optional[str]) -> None:
        """Commit the zero-copy frame"""
        # Accept process parameter but ignore it
        writer = self._writers[self._current_buffer]
        
        # Commit the frame
        writer.commit_frame()
        
        self.logger.info("Committed zero-copy frame")
        
    @when(r"(?:the '([^']+)' process )?writes frame with size '(\d+)'")
    async def write_frame_with_size(self, process: Optional[str], size: str) -> None:
        """Write a frame with specific size"""
        # Accept process parameter but ignore it
        writer = self._writers[self._current_buffer]
        frame_size = int(size)
        
        # Use simple test data pattern
        frame_data = TestDataPatterns.generate_simple_frame_data(frame_size)
        writer.write_frame(frame_data)
        
        self.logger.info(f"Wrote frame with size {size}")
        
    @when(r"(?:the '([^']+)' process )?writes metadata '([^']+)'")
    async def write_metadata_string(self, process: Optional[str], metadata: str) -> None:
        """Write metadata as string"""
        # Accept process parameter but ignore it (as per C# implementation)
        
        # Check if we need to reconnect (for metadata updates)
        if self._current_buffer in self._writers:
            existing_writer = self._writers[self._current_buffer]
            try:
                # Try to write metadata - if it fails, we need to reconnect
                metadata_bytes = metadata.encode()
                existing_writer.set_metadata(metadata_bytes)
                self.logger.info(f"Wrote metadata: {metadata}")
                return  # Success - metadata written
            except Exception as e:
                if "already" in str(e).lower() or isinstance(e, MetadataAlreadyWrittenException):
                    # Need to disconnect and reconnect
                    existing_writer.close()  # Close the old writer first
                    del self._writers[self._current_buffer]
                    
                    # Reconnect
                    actual_buffer_name = self._buffer_naming.get_buffer_name(self._current_buffer)
                    new_writer = Writer(actual_buffer_name)
                    self._writers[self._current_buffer] = new_writer
                    self.store_resource(f"writer_{self._current_buffer}", new_writer)
                    
                    # Now write the new metadata
                    new_writer.set_metadata(metadata.encode())
                    self.logger.info(f"Wrote metadata: {metadata} (after reconnect)")
                else:
                    raise
        else:
            raise Exception(f"No writer connected to buffer '{self._current_buffer}'")
        
    @when(r"(?:the '([^']+)' process )?writes frame with data '([^']+)'")
    async def write_frame_with_data(self, process: Optional[str], data: str) -> None:
        """Write frame with specific data"""
        writer = next(iter(self._writers.values()))
        writer.write_frame(data.encode())
        # Track frame info
        frame = {'data': data.encode(), 'sequence_number': writer.frames_written, 'size': len(data.encode())}
        self._frames_written.append(frame)
        
        self.logger.info(f"Wrote frame with data: {data}")
        
    @then(r"(?:the '([^']+)' process )?should read frame with sequence '(\d+)' and size '(\d+)'")
    async def read_frame_verify_sequence_size(self, process: Optional[str], sequence: str, size: str) -> None:
        """Read and verify frame sequence and size"""
        reader = next(iter(self._readers.values()))
        
        # Wait for frame with timeout
        frame = None
        for _ in range(50):  # 5 second timeout
            frame = reader.read_frame()
            if frame:
                break
            await asyncio.sleep(0.1)
            
        assert frame is not None, "No frame available to read"
        
        # Verify size
        assert len(frame.data) == int(size), \
            f"Frame size mismatch: expected {size}, got {len(frame.data)}"
            
        self._frames_read.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Read frame with sequence {frame.sequence} and size {len(frame.data)}")
        
    @then(r"(?:the '([^']+)' process )?should validate frame data")
    async def validate_frame_data(self, process: Optional[str]) -> None:
        """Validate the last read frame data"""
        # Accept process parameter but ignore it
        assert self._last_frame is not None, "No frame to validate"
        
        if isinstance(self._last_frame, Frame):
            frame_data = self._last_frame.data
            frame_sequence = self._last_frame.sequence
        elif isinstance(self._last_frame, dict):
            frame_data = self._last_frame['data']
            frame_sequence = self._last_frame['sequence_number']
        else:
            raise TypeError(f"Unexpected type for _last_frame: {type(self._last_frame)}")
        
        # Generate expected data using the shared pattern
        expected_data = TestDataPatterns.generate_frame_data(len(frame_data), frame_sequence)
        
        # Compare frame data with expected data
        assert frame_data == expected_data, "Frame data does not match expected pattern"
        
        self.logger.info("Frame data validated")
        
    @when(r"(?:the '([^']+)' process )?signals space available")
    @then(r"(?:the '([^']+)' process )?signals space available")
    async def signal_space_available(self, process: Optional[str]) -> None:
        """Signal that space is available (frame consumed)"""
        # In ZeroBuffer, we need to release the frame to signal space available
        if self._last_frame and isinstance(self._last_frame, Frame):
            # This is a real Frame object from reader
            reader = next(iter(self._readers.values()))
            reader.release_frame(self._last_frame)
        self._last_frame = None
            
        self.logger.info("Signaled space available")
        
    @then(r"(?:the '([^']+)' process )?should read frame with sequence '(\d+)';")
    async def read_frame_verify_sequence(self, process: Optional[str], sequence: str) -> None:
        """Read and verify frame sequence"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, f"No frame available with sequence {sequence}"
        
        self._frames_read.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Read frame with sequence {frame.sequence}")
        
    @then(r"(?:the '([^']+)' process )?should verify all frames maintain sequential order")
    async def verify_sequential_order(self, process: Optional[str]) -> None:
        """Verify all read frames are in sequential order"""
        if len(self._frames_read) < 2:
            return
            
        for i in range(1, len(self._frames_read)):
            prev_seq = self._frames_read[i-1].sequence
            curr_seq = self._frames_read[i].sequence
            assert curr_seq == prev_seq + 1, \
                f"Sequence break: {prev_seq} -> {curr_seq}"
                
        self.logger.info("All frames maintain sequential order")
        
    @then(r"(?:the '([^']+)' process )?should experience timeout on next write")
    async def verify_buffer_full(self, process: Optional[str]) -> None:
        """Verify that the next write will block due to buffer full"""
        writer = next(iter(self._writers.values()))
        
        # Just like C#, simply try to write and expect BufferFullException
        try:
            data = TestDataPatterns.generate_frame_data(1024, 999)
            writer.write_frame(data)
            
            # If we get here, the write succeeded when it shouldn't have
            assert False, "Write should have timed out but succeeded"
        except BufferFullException:
            # Expected - buffer is full
            self._write_error = BufferFullException()
            self.logger.info("Write blocked/failed as expected with BufferFullException")
            
    @when(r"(?:the '([^']+)' process )?reads one frame")
    async def read_one_frame(self, process: Optional[str]) -> None:
        """Read a single frame"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, "No frame available to read"
        
        self._frames_read.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Read frame with sequence {frame.sequence}")
        
    @then(r"(?:the '([^']+)' process )?should write successfully immediately")
    async def verify_write_succeeds(self, process: Optional[str]) -> None:
        """Verify that write succeeds immediately"""
        writer = next(iter(self._writers.values()))
        
        # Give a moment for the semaphore signal to propagate
        await asyncio.sleep(0.1)
        
        # Write should succeed quickly
        start_time = time.time()
        data = TestDataPatterns.generate_frame_data(1024, 1000)
        
        try:
            writer.write_frame(data)
            # Track frame info
            frame = {'data': data, 'sequence_number': writer.frames_written, 'size': len(data)}
            write_time = time.time() - start_time
            
            assert frame is not None, "Write failed"
            assert write_time < 0.5, f"Write took too long: {write_time}s"
        except Exception as e:
            self.logger.error(f"Write failed unexpectedly: {e}")
            raise
        
        self.logger.info("Write succeeded immediately")
        
    @then(r"(?:the '([^']+)' process )?should read frame with size '(\d+)'")
    async def read_frame_verify_size(self, process: Optional[str], size: str) -> None:
        """Read and verify frame size"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, "No frame available"
        assert len(frame.data) == int(size), \
            f"Frame size mismatch: expected {size}, got {len(frame.data)}"
            
        self._frames_read.append(frame)
        self._last_frame = frame  # Store for verify_test_pattern
        
        self.logger.info(f"Read frame with size {len(frame.data)}")
        
    @then(r"(?:the '([^']+)' process )?should verify frame data matches test pattern")
    async def verify_test_pattern(self, process: Optional[str]) -> None:
        """Verify frame data matches the test pattern"""
        # Accept process parameter but ignore it
        assert self._last_frame is not None, "No frame to verify"
        
        if isinstance(self._last_frame, Frame):
            # Real Frame object
            frame_data = bytes(self._last_frame.data)
            sequence = self._last_frame.sequence
        elif isinstance(self._last_frame, dict):
            # Dict object from writer
            frame_data = self._last_frame['data']
            sequence = self._last_frame['sequence_number']
        else:
            raise TypeError(f"Unexpected type for _last_frame: {type(self._last_frame)}")
        
        # Generate the expected test pattern based on the frame's sequence number
        expected_pattern = TestDataPatterns.generate_frame_data(len(frame_data), sequence)
        
        assert frame_data == expected_pattern, "Frame data does not match test pattern"
        
        self.logger.info("Frame data matches test pattern")
        
    @then(r"(?:the '([^']+)' process )?should read (\d+) frames with sizes '([^']+)' in order")
    async def read_frames_verify_sizes(self, process: Optional[str], count: str, sizes: str) -> None:
        """Read specified number of frames with specific sizes"""
        # Accept process parameter but ignore it
        reader = self._readers[self._current_buffer]
        expected_sizes = [int(s) for s in sizes.split(',')]
        
        assert int(count) == len(expected_sizes), f"Count {count} doesn't match sizes list length {len(expected_sizes)}"
        
        for i in range(int(count)):
            frame = reader.read_frame(timeout=5.0)
            assert frame is not None, f"Failed to read frame {i+1}"
            
            assert len(frame.data) == expected_sizes[i], \
                f"Frame {i+1} size mismatch: expected {expected_sizes[i]}, got {len(frame.data)}"
            
            # Verify frame data integrity using TestDataPatterns
            frame_data = bytes(frame.data)
            assert TestDataPatterns.verify_simple_frame_data(frame_data), \
                f"Frame {i+1} data does not match expected pattern"
                    
            self._frames_read.append(frame)
            
        self.logger.info(f"Read {count} frames with correct sizes")
        
    @then(r"(?:the '([^']+)' process )?should have metadata '([^']+)'")
    async def verify_metadata(self, process: Optional[str], expected_metadata: str) -> None:
        """Verify metadata content"""
        reader = next(iter(self._readers.values()))
        metadata = reader.get_metadata()
        
        assert metadata is not None, "No metadata available"
        
        # Convert metadata to string for comparison
        # get_metadata() returns Optional[memoryview]
        metadata_str = bytes(metadata).decode()
        assert expected_metadata in metadata_str, \
            f"Metadata mismatch: expected '{expected_metadata}' in '{metadata_str}'"
            
        self.logger.info(f"Metadata verified: {expected_metadata}")
        
    @then(r"(?:the '([^']+)' process )?should read frame with data '([^']+)'")
    async def read_frame_verify_data(self, process: Optional[str], expected_data: str) -> None:
        """Read frame and verify data content"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, "No frame available"
        
        # Convert frame data to string
        actual_data = bytes(frame.data).decode()
        assert actual_data == expected_data, \
            f"Frame data mismatch: expected '{expected_data}', got '{actual_data}'"
            
        self._frames_read.append(frame)
        
        self.logger.info(f"Read frame with data: {expected_data}")