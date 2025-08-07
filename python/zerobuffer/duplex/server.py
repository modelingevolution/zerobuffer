"""
Duplex Channel Server implementations
"""

import threading
import time
from typing import Callable, Optional, Awaitable
import asyncio
import logging
from ..reader import Reader
from ..writer import Writer
from ..types import BufferConfig, Frame
from ..exceptions import ZeroBufferException, ReaderDeadException, WriterDeadException
from .interfaces import IImmutableDuplexServer, IMutableDuplexServer
from .processing_mode import ProcessingMode
from ..logging_config import get_logger


class ImmutableDuplexServer(IImmutableDuplexServer):
    """Server that processes immutable requests and returns new response data"""
    
    def __init__(self, channel_name: str, config: BufferConfig, logger: Optional[logging.Logger] = None) -> None:
        """
        Create an immutable duplex server
        
        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            logger: Optional logger
        """
        self._channel_name = channel_name
        self._request_buffer_name = f"{channel_name}_request"
        self._response_buffer_name = f"{channel_name}_response"
        self._config = config
        self._logger = logger
        
        self._request_reader: Optional[Reader] = None
        self._response_writer: Optional[Writer] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._handler: Optional[Callable[[Frame], bytes]] = None
        self._lock = threading.Lock()
    
    def _is_running(self) -> bool:
        """Check if server should keep running (thread-safe check)"""
        return bool(self._running)
    
    def start(self, handler: Callable[[Frame], bytes], mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """Start processing requests"""
        with self._lock:
            if self._running:
                raise ZeroBufferException("Server is already running")
            
            self._handler = handler
            self._running = True
            
            if mode == ProcessingMode.SINGLE_THREAD:
                # Start in separate thread
                self._thread = threading.Thread(target=self._process_requests)
                self._thread.daemon = True
                self._thread.start()
            elif mode == ProcessingMode.THREAD_POOL:
                # Not yet implemented
                raise NotImplementedError("THREAD_POOL mode is not yet implemented")
            else:
                raise ValueError(f"Invalid processing mode: {mode}")
    
    async def start_async(self, handler: Callable[[Frame], Awaitable[bytes]]) -> None:
        """Start processing asynchronously"""
        if self._running:
            raise ZeroBufferException("Server is already running")
        
        self._running = True
        
        # Create buffers
        self._request_reader = Reader(self._request_buffer_name, self._config)
        
        # Wait for client to connect
        while self._running and not self._request_reader.is_writer_connected():
            await asyncio.sleep(0.1)
        
        if not self._running:
            return
        
        # Connect to response buffer
        self._response_writer = Writer(self._response_buffer_name)
        
        # Process requests asynchronously
        try:
            while self._running:
                # Read request
                frame = self._request_reader.read_frame(timeout=0.1)
                if frame is None:
                    continue
                
                try:
                    # Process request asynchronously
                    response_data = await handler(frame)
                    
                    # Write response with same sequence number
                    # Note: We need to preserve the sequence number from request
                    # This requires enhancing Writer to support custom sequence numbers
                    self._response_writer.write_frame(response_data)
                    
                finally:
                    self._request_reader.release_frame(frame)
                    
        except (ReaderDeadException, WriterDeadException):
            if self._logger:
                self._logger.info("Client disconnected")
        finally:
            self._cleanup()
    
    def _process_requests(self):
        """Process requests synchronously"""
        try:
            # Create request buffer as reader
            self._request_reader = Reader(self._request_buffer_name, self._config)
            
            # Wait for client to create response buffer and connect as writer
            timeout_start = time.time()
            while self._running and not self._request_reader.is_writer_connected():
                if time.time() - timeout_start > 30:  # 30 second timeout
                    if self._logger:
                        self._logger.warning("Timeout waiting for client")
                    return
                time.sleep(0.1)
            
            # Connect to response buffer as writer
            retry_count = 0
            while retry_count < 50:  # 5 seconds
                if not self._is_running():
                    return
                try:
                    self._response_writer = Writer(self._response_buffer_name)
                    break
                except:
                    retry_count += 1
                    time.sleep(0.1)
            
            if self._response_writer is None:
                if self._logger:
                    self._logger.error("Failed to connect to response buffer")
                return
            
            # Process requests
            while True:
                if not self._is_running():
                    break
                try:
                    # Read request with short timeout to allow checking _running flag
                    frame = self._request_reader.read_frame(timeout=0.1)
                    if frame is None:
                        continue
                    
                    # Process request
                    if self._handler is None:
                        raise RuntimeError("Handler not set")
                    response_data = self._handler(frame)
                    
                    # Write response with same sequence number
                    # For now, we just write the response
                    # TODO: Enhance Writer to support custom sequence numbers
                    self._response_writer.write_frame(response_data)
                    
                    # Release frame
                    self._request_reader.release_frame(frame)
                    
                except (ReaderDeadException, WriterDeadException):
                    if self._logger:
                        self._logger.info("Client disconnected")
                    break
                except Exception as e:
                    if self._logger:
                        self._logger.error(f"Error processing request: {e}")
                    
        finally:
            self._cleanup()
    
    def _cleanup(self) -> None:
        """Clean up resources"""
        if self._response_writer:
            self._response_writer.close()
            self._response_writer = None
        
        if self._request_reader:
            self._request_reader.close()
            self._request_reader = None
    
    def stop(self) -> None:
        """Stop processing"""
        with self._lock:
            self._running = False
        
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=2.0)
        
        self._cleanup()
    
    @property
    def is_running(self) -> bool:
        """Check if running"""
        return self._running


class MutableDuplexServer(IMutableDuplexServer):
    """Server that mutates request data in-place (zero-copy)"""
    
    def __init__(self, channel_name: str, config: BufferConfig, logger: Optional[logging.Logger] = None) -> None:
        """
        Create a mutable duplex server
        
        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            logger: Optional logger
        """
        self._channel_name = channel_name
        self._request_buffer_name = f"{channel_name}_request"
        self._response_buffer_name = f"{channel_name}_response"
        self._config = config
        self._logger = logger
        
        self._request_reader: Optional[Reader] = None
        self._response_writer: Optional[Writer] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._handler: Optional[Callable[[Frame], None]] = None
        self._lock = threading.Lock()
    
    def _is_running(self) -> bool:
        """Check if server should keep running (thread-safe check)"""
        return bool(self._running)
    
    def start(self, handler: Callable[[Frame], None], mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """Start processing with mutable handler"""
        with self._lock:
            if self._running:
                raise ZeroBufferException("Server is already running")
            
            self._handler = handler
            self._running = True
            
            if mode == ProcessingMode.SINGLE_THREAD:
                # Start in separate thread
                self._thread = threading.Thread(target=self._process_requests)
                self._thread.daemon = True
                self._thread.start()
            elif mode == ProcessingMode.THREAD_POOL:
                # Not yet implemented
                raise NotImplementedError("THREAD_POOL mode is not yet implemented")
            else:
                raise ValueError(f"Invalid processing mode: {mode}")
    
    def _process_requests(self):
        """Process requests with in-place mutation"""
        try:
            # Create request buffer as reader
            self._request_reader = Reader(self._request_buffer_name, self._config)
            
            # Wait for client
            timeout_start = time.time()
            while self._running and not self._request_reader.is_writer_connected():
                if time.time() - timeout_start > 30:
                    if self._logger:
                        self._logger.warning("Timeout waiting for client")
                    return
                time.sleep(0.1)
            
            if not self._running:
                return
            
            # Connect to response buffer
            retry_count = 0
            while retry_count < 50:
                if not self._is_running():
                    return
                try:
                    self._response_writer = Writer(self._response_buffer_name)
                    break
                except:
                    retry_count += 1
                    time.sleep(0.1)
            
            if self._response_writer is None:
                if self._logger:
                    self._logger.error("Failed to connect to response buffer")
                return
            
            # Process requests
            while True:
                if not self._is_running():
                    break
                try:
                    # Read request with short timeout to allow checking _running flag
                    frame = self._request_reader.read_frame(timeout=0.1)
                    if frame is None:
                        continue
                    
                    # Process request in-place
                    # Note: In Python, we can't truly modify the frame data in-place
                    # because it's in shared memory. We need to copy it.
                    # True zero-copy would require memory-mapped access
                    
                    # Call handler to process frame
                    if self._handler is None:
                        raise RuntimeError("Handler not set")
                    self._handler(frame)
                    
                    # Write the modified data as response
                    # TODO: Support true zero-copy by using memory views
                    self._response_writer.write_frame(bytes(frame.data))
                    
                    # Release frame
                    self._request_reader.release_frame(frame)
                    
                except (ReaderDeadException, WriterDeadException):
                    if self._logger:
                        self._logger.info("Client disconnected")
                    break
                except Exception as e:
                    if self._logger:
                        self._logger.error(f"Error processing request: {e}")
                    
        finally:
            self._cleanup()
    
    def _cleanup(self) -> None:
        """Clean up resources"""
        if self._response_writer:
            self._response_writer.close()
            self._response_writer = None
        
        if self._request_reader:
            self._request_reader.close()
            self._request_reader = None
    
    def stop(self) -> None:
        """Stop processing"""
        with self._lock:
            self._running = False
        
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=2.0)
        
        self._cleanup()
    
    @property
    def is_running(self) -> bool:
        """Check if running"""
        return self._running