"""
Duplex Channel Client implementation
"""

import threading
from typing import Tuple, Optional, Any
from ..reader import Reader
from ..writer import Writer
from ..types import BufferConfig
from ..exceptions import ZeroBufferException
from .interfaces import IDuplexClient, DuplexResponse


class DuplexClient(IDuplexClient):
    """Client implementation for duplex channels"""
    
    def __init__(self, channel_name: str):
        """
        Create a duplex client
        
        Args:
            channel_name: Name of the duplex channel
        """
        self._channel_name = channel_name
        self._request_buffer_name = f"{channel_name}_request"
        self._response_buffer_name = f"{channel_name}_response"
        
        # Create response reader first (we own the response buffer)
        # Use default config - server should have created with same config
        self._response_reader: Optional[Reader] = None
        self._request_writer: Optional[Writer] = None
        self._lock = threading.Lock()
        self._closed = False
        self._pending_sequence: Optional[int] = None
        self._pending_buffer: Optional[memoryview] = None
        
        # Initialize connections
        self._connect()
    
    def _connect(self):
        """Initialize connections to buffers"""
        # Default config matching C# defaults
        config = BufferConfig(metadata_size=4096, payload_size=256*1024*1024)
        
        # Create response buffer as reader
        self._response_reader = Reader(self._response_buffer_name, config)
        
        # Connect to request buffer as writer
        self._request_writer = Writer(self._request_buffer_name)
    
    def send_request(self, data: bytes) -> int:
        """Send a request and return sequence number"""
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Client is closed")
            
            if self._pending_buffer is not None:
                raise ZeroBufferException("Previous request not committed")
            
            # Get current sequence before writing
            # Since Writer increments after writing, we predict the sequence
            if self._request_writer is None:
                raise RuntimeError("Request writer not connected")
            current_sequence = getattr(self._request_writer, '_sequence_number', 0)
            
            # Write frame
            self._request_writer.write_frame(data)
            
            # Return the sequence number that was used
            return current_sequence
    
    def acquire_request_buffer(self, size: int) -> Tuple[int, memoryview]:
        """Acquire buffer for zero-copy write"""
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Client is closed")
            
            if self._pending_buffer is not None:
                raise ZeroBufferException("Previous request not committed")
            
            # Get current sequence before acquiring buffer
            if self._request_writer is None:
                raise RuntimeError("Request writer not connected")
            self._pending_sequence = self._request_writer._sequence_number
            
            # Get buffer from writer
            buffer = self._request_writer.get_frame_buffer(size)
            self._pending_buffer = buffer
            
            return (self._pending_sequence, buffer)
    
    def commit_request(self) -> None:
        """Commit the pending request"""
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Client is closed")
            
            if self._pending_buffer is None:
                raise ZeroBufferException("No pending request to commit")
            
            # Commit the frame
            if self._request_writer is None:
                raise RuntimeError("Request writer not connected")
            self._request_writer.commit_frame()
            self._pending_buffer = None
            self._pending_sequence = None
    
    def receive_response(self, timeout_ms: int) -> DuplexResponse:
        """Receive a response with timeout"""
        if self._closed:
            raise ZeroBufferException("Client is closed")
        
        # Read frame from response buffer
        if self._response_reader is None:
            raise RuntimeError("Response reader not connected")
        frame = self._response_reader.read_frame(timeout=timeout_ms / 1000.0)
        
        if frame is None:
            # Return invalid response
            return DuplexResponse(sequence=0, data=None)
        
        # Create response with frame data
        response = DuplexResponse(
            sequence=frame.sequence,
            data=frame.data,
            _frame=frame
        )
        
        # Note: Caller is responsible for releasing the frame
        # by calling reader.release_frame(response._frame)
        return response
    
    def release_response(self, response: DuplexResponse) -> None:
        """Release a response frame"""
        if response._frame and self._response_reader is not None:
            self._response_reader.release_frame(response._frame)
    
    @property
    def is_server_connected(self) -> bool:
        """Check if server is connected"""
        if self._closed:
            return False
        
        # Server is connected if it's reading from request buffer
        # and writing to response buffer
        if self._request_writer is None or self._response_reader is None:
            return False
        return (self._request_writer.is_reader_connected() and 
                self._response_reader.is_writer_connected())
    
    def close(self) -> None:
        """Close the client"""
        with self._lock:
            if self._closed:
                return
            
            self._closed = True
            
            if self._request_writer:
                self._request_writer.close()
            
            if self._response_reader:
                self._response_reader.close()
    
    def __enter__(self):
        """Context manager entry"""
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit"""
        self.close()
    
    def __del__(self):
        """Cleanup on deletion"""
        self.close()