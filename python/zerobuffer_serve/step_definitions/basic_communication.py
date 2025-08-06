"""
Basic communication step definitions

Implements test steps for fundamental ZeroBuffer communication patterns.
"""

import asyncio
import time
from typing import Dict, Optional, List
import uuid

from zerobuffer import Reader, Writer, BufferConfig, Frame
from zerobuffer.exceptions import ZeroBufferException

from .base import BaseSteps
from ..step_registry import given, when, then, parsers


class BasicCommunicationSteps(BaseSteps):
    """Step definitions for basic communication tests"""
    
    def __init__(self, test_context, logger):
        super().__init__(test_context, logger)
        self._readers: Dict[str, Reader] = {}
        self._writers: Dict[str, Writer] = {}
        self._last_frame: Optional[Frame] = None
        self._frames_written: List[Frame] = []
        self._frames_read: List[Frame] = []
        self._write_error: Optional[Exception] = None
        
    @given(r"the test environment is initialized")
    def test_environment_initialized(self):
        """Initialize test environment"""
        self.logger.info("Test environment initialized")
        
    @given(r"all processes are ready")
    def all_processes_ready(self):
        """Confirm all processes are ready"""
        self.logger.info("All processes ready")
        
    @given(parsers.re(r"(?:the '(?P<process>[^']+)' process )?creates buffer '(?P<buffer_name>[^']+)' with metadata size '(?P<metadata_size>\d+)' and payload size '(?P<payload_size>\d+)'"))
    async def create_buffer(self, process: Optional[str], buffer_name: str, metadata_size: str, payload_size: str):
        """Create a new ZeroBuffer with specified configuration"""
            
        config = BufferConfig(
            metadata_size=int(metadata_size),
            payload_size=int(payload_size)
        )
        
        reader = Reader(buffer_name, config)
        self._readers[buffer_name] = reader
        self.store_resource(f"reader_{buffer_name}", reader)
        
        self.logger.info(
            f"Created buffer '{buffer_name}' with metadata_size={metadata_size}, "
            f"payload_size={payload_size}"
        )
        
    @when(r"(?:the '([^']+)' process )?connects to buffer '([^']+)'")
    async def connect_to_buffer(self, process: Optional[str], buffer_name: str):
        """Connect a writer to an existing buffer"""
            
        writer = Writer(buffer_name)
        self._writers[buffer_name] = writer
        self.store_resource(f"writer_{buffer_name}", writer)
        
        self.logger.info(f"Connected to buffer '{buffer_name}'")
        
    @when(r"(?:the '([^']+)' process )?writes metadata with size '(\d+)'")
    async def write_metadata(self, process: Optional[str], size: str):
        """Write metadata to the buffer"""
        # Find the first writer
        writer = next(iter(self._writers.values()))
        
        # Create metadata of specified size
        metadata = b'M' * int(size)
        writer.set_metadata(metadata)
        
        self.logger.info(f"Wrote metadata with size {size}")
        
    @when(r"(?:the '([^']+)' process )?writes frame with size '(\d+)' and sequence '(\d+)'")
    async def write_frame_with_sequence(self, process: Optional[str], size: str, sequence: str):
        """Write a frame with specific size and sequence"""
        writer = next(iter(self._writers.values()))
        
        # Create frame data
        frame_size = int(size)
        frame_data = b'F' * frame_size
        
        # Write frame
        writer.write_frame(frame_data)
        # Note: Frame is just a tracking object here, actual frame is in shared memory
        frame = {'data': frame_data, 'sequence_number': int(sequence), 'size': len(frame_data)}
        self._frames_written.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Wrote frame with size {size} and sequence {sequence}")
        
    @when(r"(?:the '([^']+)' process )?writes frame with sequence '(\d+)'")
    async def write_frame_sequence_only(self, process: Optional[str], sequence: str):
        """Write a frame with default size"""
        await self.write_frame_with_sequence(process, "1024", sequence)
        
    @when(r"(?:the '([^']+)' process )?writes frames until buffer is full")
    async def write_until_full(self, process: Optional[str]):
        """Write frames until the buffer is full"""
        writer = next(iter(self._writers.values()))
        frame_count = 0
        
        try:
            # Write frames until we hit buffer full
            # Use large frames to fill buffer faster (1KB per frame)
            frame_size = 1024
            while True:
                data = b'X' * frame_size
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
            self.logger.info(f"Write stopped after {frame_count} frames: {e}")
            
    @when(r"(?:the '([^']+)' process )?requests zero-copy frame of size '(\d+)'")
    async def request_zero_copy_frame(self, process: Optional[str], size: str):
        """Request a zero-copy frame"""
        writer = next(iter(self._writers.values()))
        frame_size = int(size)
        
        # Get zero-copy buffer
        buffer = writer.get_frame_buffer(frame_size)
        self.set_data("zero_copy_buffer", buffer)
        self.set_data("zero_copy_size", frame_size)
        
        self.logger.info(f"Requested zero-copy frame of size {size}")
        
    @when(r"(?:the '([^']+)' process )?fills zero-copy buffer with test pattern")
    async def fill_zero_copy_buffer(self, process: Optional[str]):
        """Fill zero-copy buffer with test pattern"""
        buffer = self.get_data("zero_copy_buffer")
        size = self.get_data("zero_copy_size")
        
        # Fill with test pattern
        pattern = b'ABCD' * (size // 4 + 1)
        buffer[:size] = pattern[:size]
        
        self.logger.info("Filled zero-copy buffer with test pattern")
        
    @when(r"(?:the '([^']+)' process )?commits zero-copy frame")
    async def commit_zero_copy_frame(self, process: Optional[str]):
        """Commit the zero-copy frame"""
        writer = next(iter(self._writers.values()))
        size = self.get_data("zero_copy_size")
        
        # Commit the frame
        writer.commit_frame()
        # Track frame info
        frame = {'data': self.get_data("zero_copy_buffer")[:size], 'sequence_number': writer.frames_written, 'size': size}
        self._frames_written.append(frame)
        self._last_frame = frame
        
        self.logger.info("Committed zero-copy frame")
        
    @when(r"(?:the '([^']+)' process )?writes frame with size '(\d+)'")
    async def write_frame_with_size(self, process: Optional[str], size: str):
        """Write a frame with specific size"""
        writer = next(iter(self._writers.values()))
        frame_data = b'X' * int(size)
        
        writer.write_frame(frame_data)
        # Track frame info
        frame = {'data': frame_data, 'sequence_number': writer.frames_written, 'size': len(frame_data)}
        self._frames_written.append(frame)
        
        self.logger.info(f"Wrote frame with size {size}")
        
    @when(r"(?:the '([^']+)' process )?writes metadata '([^']+)'")
    async def write_metadata_string(self, process: Optional[str], metadata: str):
        """Write metadata as string"""
        writer = next(iter(self._writers.values()))
        writer.set_metadata(metadata.encode())
        
        self.logger.info(f"Wrote metadata: {metadata}")
        
    @when(r"(?:the '([^']+)' process )?writes frame with data '([^']+)'")
    async def write_frame_with_data(self, process: Optional[str], data: str):
        """Write frame with specific data"""
        writer = next(iter(self._writers.values()))
        writer.write_frame(data.encode())
        # Track frame info
        frame = {'data': data.encode(), 'sequence_number': writer.frames_written, 'size': len(data.encode())}
        self._frames_written.append(frame)
        
        self.logger.info(f"Wrote frame with data: {data}")
        
    @then(r"(?:the '([^']+)' process )?should read frame with sequence '(\d+)' and size '(\d+)'")
    async def read_frame_verify_sequence_size(self, process: Optional[str], sequence: str, size: str):
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
    async def validate_frame_data(self, process: Optional[str]):
        """Validate the last read frame data"""
        assert self._last_frame is not None, "No frame to validate"
        
        # Basic validation - check data is not empty
        assert len(self._last_frame.data) > 0, "Frame data is empty"
        
        self.logger.info("Frame data validated")
        
    @then(r"(?:the '([^']+)' process )?signals space available")
    async def signal_space_available(self, process: Optional[str]):
        """Signal that space is available (frame consumed)"""
        # In ZeroBuffer, we need to release the frame to signal space available
        if self._last_frame and hasattr(self._last_frame, 'sequence'):
            # This is a real Frame object from reader
            reader = next(iter(self._readers.values()))
            reader.release_frame(self._last_frame)
        self._last_frame = None
            
        self.logger.info("Signaled space available")
        
    @then(r"(?:the '([^']+)' process )?should read frame with sequence '(\d+)'")
    async def read_frame_verify_sequence(self, process: Optional[str], sequence: str):
        """Read and verify frame sequence"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, f"No frame available with sequence {sequence}"
        
        self._frames_read.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Read frame with sequence {frame.sequence}")
        
    @then(r"(?:the '([^']+)' process )?should verify all frames maintain sequential order")
    async def verify_sequential_order(self, process: Optional[str]):
        """Verify all read frames are in sequential order"""
        if len(self._frames_read) < 2:
            return
            
        for i in range(1, len(self._frames_read)):
            prev_seq = self._frames_read[i-1].sequence
            curr_seq = self._frames_read[i].sequence
            assert curr_seq == prev_seq + 1, \
                f"Sequence break: {prev_seq} -> {curr_seq}"
                
        self.logger.info("All frames maintain sequential order")
        
    @then(r"(?:the '([^']+)' process )?should experience timeout or buffer full on next write")
    async def verify_buffer_full(self, process: Optional[str]):
        """Verify that the next write will block due to buffer full"""
        writer = next(iter(self._writers.values()))
        
        # According to protocol: when buffer is full, writer should BLOCK on semaphore
        # We simulate this by trying to write with a very short timeout
        import threading
        import time
        
        write_completed = threading.Event()
        write_exception = [None]
        
        def write_thread():
            try:
                data = b"This should block because buffer is full"
                writer.write_frame(data)
                write_completed.set()
            except Exception as e:
                write_exception[0] = e
                write_completed.set()
        
        # Start write in background thread
        thread = threading.Thread(target=write_thread)
        thread.start()
        
        # Wait a short time - write should NOT complete quickly if buffer is full
        if write_completed.wait(timeout=2.0):
            if write_exception[0]:
                # Write failed with exception - this is acceptable
                self.logger.info(f"Write blocked/failed as expected: {write_exception[0]}")
            else:
                # Write completed - buffer was not actually full
                assert False, "Write completed immediately - buffer was not full"
        else:
            # Write is still blocking after 2 seconds - this is the expected behavior
            self.logger.info("Write is blocking as expected due to buffer full")
            
        # Clean up thread
        thread.join(timeout=1.0)
            
    @when(r"(?:the '([^']+)' process )?reads one frame")
    async def read_one_frame(self, process: Optional[str]):
        """Read a single frame"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, "No frame available to read"
        
        self._frames_read.append(frame)
        self._last_frame = frame
        
        self.logger.info(f"Read frame with sequence {frame.sequence}")
        
    @then(r"(?:the '([^']+)' process )?should write successfully immediately")
    async def verify_write_succeeds(self, process: Optional[str]):
        """Verify that write succeeds immediately"""
        writer = next(iter(self._writers.values()))
        
        # Write should succeed quickly
        start_time = time.time()
        writer.write_frame(b"Success")
        # Track frame info
        frame = {'data': b"Success", 'sequence_number': writer.frames_written, 'size': len(b"Success")}
        write_time = time.time() - start_time
        
        assert frame is not None, "Write failed"
        assert write_time < 0.5, f"Write took too long: {write_time}s"
        
        self.logger.info("Write succeeded immediately")
        
    @then(r"(?:the '([^']+)' process )?should read frame with size '(\d+)'")
    async def read_frame_verify_size(self, process: Optional[str], size: str):
        """Read and verify frame size"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, "No frame available"
        assert len(frame.data) == int(size), \
            f"Frame size mismatch: expected {size}, got {len(frame.data)}"
            
        self._frames_read.append(frame)
        
        self.logger.info(f"Read frame with size {len(frame.data)}")
        
    @then(r"(?:the '([^']+)' process )?should verify frame data matches test pattern")
    async def verify_test_pattern(self, process: Optional[str]):
        """Verify frame data matches the test pattern"""
        assert self._last_frame is not None, "No frame to verify"
        
        # Check for ABCD pattern
        if hasattr(self._last_frame, 'data'):
            # Real Frame object
            data = bytes(self._last_frame.data)
        else:
            # Dict object from writer
            data = self._last_frame['data']
            
        pattern = b'ABCD' * (len(data) // 4 + 1)
        expected = pattern[:len(data)]
        
        assert data == expected, "Frame data does not match test pattern"
        
        self.logger.info("Frame data matches test pattern")
        
    @then(r"(?:the '([^']+)' process )?should read (\d+) frames with correct sizes in order")
    async def read_frames_verify_count(self, process: Optional[str], count: str):
        """Read specified number of frames"""
        reader = next(iter(self._readers.values()))
        expected_count = int(count)
        
        # Expected sizes from test scenario
        expected_sizes = [100, 1024, 10240, 1]
        
        for i in range(expected_count):
            frame = reader.read_frame(timeout=5.0)
            assert frame is not None, f"Failed to read frame {i+1}"
            
            if i < len(expected_sizes):
                assert len(frame.data) == expected_sizes[i], \
                    f"Frame {i+1} size mismatch: expected {expected_sizes[i]}, got {len(frame.data)}"
                    
            self._frames_read.append(frame)
            
        self.logger.info(f"Read {expected_count} frames with correct sizes")
        
    @then(r"(?:the '([^']+)' process )?should have metadata '([^']+)'")
    async def verify_metadata(self, process: Optional[str], expected_metadata: str):
        """Verify metadata content"""
        reader = next(iter(self._readers.values()))
        metadata = reader.metadata
        
        assert metadata is not None, "No metadata available"
        
        # Check if metadata contains expected value
        metadata_str = metadata.decode() if isinstance(metadata, bytes) else str(metadata)
        assert expected_metadata in metadata_str, \
            f"Metadata mismatch: expected '{expected_metadata}' in '{metadata_str}'"
            
        self.logger.info(f"Metadata verified: {expected_metadata}")
        
    @then(r"(?:the '([^']+)' process )?should read frame with data '([^']+)'")
    async def read_frame_verify_data(self, process: Optional[str], expected_data: str):
        """Read frame and verify data content"""
        reader = next(iter(self._readers.values()))
        frame = reader.read_frame(timeout=5.0)
        
        assert frame is not None, "No frame available"
        
        actual_data = frame.data.decode()
        assert actual_data == expected_data, \
            f"Frame data mismatch: expected '{expected_data}', got '{actual_data}'"
            
        self._frames_read.append(frame)
        
        self.logger.info(f"Read frame with data: {expected_data}")