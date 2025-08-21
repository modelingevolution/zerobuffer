"""
Duplex Channel Server implementations
"""

import threading
import time
from typing import Callable, Optional, Awaitable, List, Union
import asyncio
import logging
from ..reader import Reader
from ..writer import Writer
from ..types import BufferConfig, Frame
from ..exceptions import ZeroBufferException, ReaderDeadException, WriterDeadException
from .interfaces import IImmutableDuplexServer
from .processing_mode import ProcessingMode
from ..logging_config import get_logger
from ..error_event_args import ErrorEventArgs
from ..logger_factory import ILoggerFactory, get_default_factory


class ImmutableDuplexServer(IImmutableDuplexServer):
    """Server that processes immutable requests and returns new response data"""
    
    def __init__(self, channel_name: str, config: BufferConfig, timeout: Optional[float] = None, logger: Optional[Union[logging.Logger, ILoggerFactory]] = None) -> None:
        """
        Create an immutable duplex server
        
        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            timeout: Optional timeout in seconds for read operations (None for default)
            logger: Optional logger or logger factory
        """
        self._channel_name = channel_name
        self._request_buffer_name = f"{channel_name}_request"
        self._response_buffer_name = f"{channel_name}_response"
        self._config = config
        self._timeout = timeout if timeout is not None else 5.0  # Default 5 seconds
        
        # Handle logger or logger factory
        if logger is None:
            self._logger = get_default_factory().create_logger(self.__class__.__name__)
        elif isinstance(logger, ILoggerFactory):
            self._logger = logger.create_logger(self.__class__.__name__)
        else:
            self._logger = logger
        
        self._request_reader: Optional[Reader] = None
        self._response_writer: Optional[Writer] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._handler: Optional[Callable[[Frame], bytes]] = None
        self._lock = threading.Lock()
        self._error_handlers: List[Callable[[ErrorEventArgs], None]] = []
    
    def _is_running(self) -> bool:
        """Check if server should keep running (thread-safe check)"""
        return bool(self._running)
    
    def add_error_handler(self, handler: Callable[[ErrorEventArgs], None]) -> None:
        """Add an error event handler"""
        with self._lock:
            if handler not in self._error_handlers:
                self._error_handlers.append(handler)
    
    def remove_error_handler(self, handler: Callable[[ErrorEventArgs], None]) -> None:
        """Remove an error event handler"""
        with self._lock:
            if handler in self._error_handlers:
                self._error_handlers.remove(handler)
    
    def _invoke_error_handlers(self, exception: Exception) -> None:
        """Invoke all registered error handlers"""
        event_args = ErrorEventArgs(exception)
        # Make a copy of handlers to avoid issues if handlers modify the list
        handlers = self._error_handlers.copy()
        for handler in handlers:
            try:
                handler(event_args)
            except Exception as e:
                # Log but don't propagate exceptions from error handlers
                if self._logger:
                    self._logger.error(f"Error in error handler: {e}")
    
    def start(self, handler: Callable[[Frame], bytes], on_init: Optional[Callable[[memoryview], None]] = None, mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """Start processing requests"""
        with self._lock:
            if self._running:
                raise ZeroBufferException("Server is already running")
            
            self._handler = handler
            self._on_init = on_init
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
                frame = self._request_reader.read_frame(timeout=self._timeout)
                if frame is None:
                    continue
                
                # Use context manager for RAII - frame is disposed on exit
                with frame:
                    # Process request asynchronously
                    response_data = await handler(frame)
                    
                    # Write response with same sequence number
                    # Note: We need to preserve the sequence number from request
                    # This requires enhancing Writer to support custom sequence numbers
                    self._response_writer.write_frame(response_data)
                    
        except (ReaderDeadException, WriterDeadException) as e:
            if self._logger:
                self._logger.info("Client disconnected")
            self._invoke_error_handlers(e)
        except Exception as e:
            if self._logger:
                self._logger.error(f"Error in async processing: {e}")
            self._invoke_error_handlers(e)
        finally:
            self._cleanup()
    
    def _process_requests(self) -> None:
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
            
            # Call initialization callback with metadata if provided
            if hasattr(self, '_on_init') and self._on_init and self._request_reader:
                metadata = self._request_reader.get_metadata()
                if metadata:
                    try:
                        self._on_init(metadata)
                    except Exception as e:
                        if self._logger:
                            self._logger.error(f"Error in initialization callback: {e}")
                        self._invoke_error_handlers(e)
            
            # Process requests
            while True:
                if not self._is_running():
                    break
                try:
                    # Read request with configurable timeout
                    frame = self._request_reader.read_frame(timeout=self._timeout)
                    if frame is None:
                        continue
                    
                    # Use context manager for RAII - frame is disposed on exit
                    with frame:
                        # Process request
                        if self._handler is None:
                            raise RuntimeError("Handler not set")
                        response_data = self._handler(frame)
                        
                        # Write response with same sequence number
                        # For now, we just write the response
                        # TODO: Enhance Writer to support custom sequence numbers
                        self._response_writer.write_frame(response_data)
                    
                except (ReaderDeadException, WriterDeadException) as e:
                    if self._logger:
                        self._logger.info("Client disconnected")
                    self._invoke_error_handlers(e)
                    break
                except Exception as e:
                    if self._logger:
                        self._logger.error(f"Error processing request: {e}")
                    self._invoke_error_handlers(e)
                    # Continue processing after non-fatal errors
                    
        except Exception as e:
            if self._logger:
                self._logger.error(f"Fatal error in processing thread: {e}")
            self._invoke_error_handlers(e)
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

