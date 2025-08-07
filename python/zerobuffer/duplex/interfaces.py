"""
Duplex Channel interfaces matching C# API
"""

from abc import ABC, abstractmethod
from typing import Tuple, Callable, Optional, Awaitable
from dataclasses import dataclass
from ..types import Frame, BufferConfig
from .processing_mode import ProcessingMode


@dataclass
class DuplexResponse:
    """Response from duplex channel with sequence number and data"""
    sequence: int
    data: Optional[memoryview]
    _frame: Optional[Frame] = None
    
    @property
    def is_valid(self) -> bool:
        """Check if response is valid"""
        return self.data is not None and len(self.data) > 0
    
    def to_bytes(self) -> bytes:
        """Convert to bytes (creates a copy)"""
        if self.data is None:
            return b''
        return bytes(self.data)
    
    def __len__(self) -> int:
        """Get response data size"""
        return len(self.data) if self.data else 0


class IDuplexClient(ABC):
    """Client-side interface for sending requests and receiving responses"""
    
    @abstractmethod
    def send_request(self, data: bytes) -> int:
        """
        Send a request with data copy and return the sequence number for correlation.
        This method returns immediately after writing to the request buffer.
        
        Args:
            data: Request data to send
            
        Returns:
            Sequence number for correlating the response
        """
        pass
    
    @abstractmethod
    def acquire_request_buffer(self, size: int) -> Tuple[int, memoryview]:
        """
        Acquire buffer for zero-copy write. Returns (sequence_number, buffer).
        Call commit_request() after writing to send the request.
        
        Args:
            size: Size of buffer to acquire
            
        Returns:
            Tuple of (sequence_number, buffer)
        """
        pass
    
    @abstractmethod
    def commit_request(self) -> None:
        """Commit the request after writing to the acquired buffer"""
        pass
    
    @abstractmethod
    def receive_response(self, timeout_ms: int) -> DuplexResponse:
        """
        Receive a response frame. This method blocks until a response is available or timeout.
        The caller is responsible for correlating responses using the sequence number in the frame.
        
        Args:
            timeout_ms: Timeout in milliseconds
            
        Returns:
            DuplexResponse with sequence number and data
        """
        pass
    
    @property
    @abstractmethod
    def is_server_connected(self) -> bool:
        """Check if server is connected to the request buffer"""
        pass
    
    @abstractmethod
    def close(self) -> None:
        """Close the client and release resources"""
        pass
    
    @abstractmethod
    def __enter__(self):
        """Context manager entry"""
        pass
    
    @abstractmethod
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit"""
        pass


class IDuplexServer(ABC):
    """Base server-side interface with common functionality"""
    
    @abstractmethod
    def stop(self) -> None:
        """Stop processing"""
        pass
    
    @property
    @abstractmethod
    def is_running(self) -> bool:
        """Check if running"""
        pass


class IImmutableDuplexServer(IDuplexServer):
    """Server that processes immutable requests and returns new response data"""
    
    @abstractmethod
    def start(self, handler: Callable[[Frame], bytes], mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """
        Start processing requests with a handler that returns new data.
        
        Args:
            handler: Function that takes a Frame and returns response bytes
            mode: Processing mode - SINGLE_THREAD runs in one background thread,
                  THREAD_POOL would process each request in a thread pool (not yet implemented)
        """
        pass
    
    @abstractmethod
    async def start_async(self, handler: Callable[[Frame], Awaitable[bytes]]) -> None:
        """
        Start processing asynchronously with async handler.
        
        Args:
            handler: Async function that takes a Frame and returns response bytes
        """
        pass


class IMutableDuplexServer(IDuplexServer):
    """Server that mutates request data in-place (zero-copy)"""
    
    @abstractmethod
    def start(self, handler: Callable[[Frame], None], mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """
        Start processing with mutable handler that modifies frame data in-place.
        
        Args:
            handler: Function that takes a Frame and modifies it in-place
            mode: Processing mode - SINGLE_THREAD runs in one background thread,
                  THREAD_POOL would process each request in a thread pool (not yet implemented)
        """
        pass


class IDuplexChannelFactory(ABC):
    """Factory for creating duplex channels"""
    
    @abstractmethod
    def create_immutable_server(self, channel_name: str, config: BufferConfig) -> IImmutableDuplexServer:
        """
        Create an immutable server (processes immutable requests, returns new response data)
        
        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            
        Returns:
            IImmutableDuplexServer instance
        """
        pass
    
    @abstractmethod
    def create_mutable_server(self, channel_name: str, config: BufferConfig) -> IMutableDuplexServer:
        """
        Create a mutable server (mutates request data in-place)
        
        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            
        Returns:
            IMutableDuplexServer instance
        """
        pass
    
    @abstractmethod
    def create_client(self, channel_name: str) -> IDuplexClient:
        """
        Connect to existing duplex channel (client-side)
        
        Args:
            channel_name: Name of the duplex channel
            
        Returns:
            IDuplexClient instance
        """
        pass