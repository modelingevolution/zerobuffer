"""
Duplex Channel Server implementations
"""

import threading
import time
from typing import Callable, Optional, List
import logging
from ..reader import Reader
from ..writer import Writer
from ..types import BufferConfig, Frame
from ..exceptions import ZeroBufferException, ReaderDeadException, WriterDeadException
from .interfaces import IImmutableDuplexServer
from .processing_mode import ProcessingMode
from ..error_event_args import ErrorEventArgs

# Module logger
logger = logging.getLogger(__name__)


class ImmutableDuplexServer(IImmutableDuplexServer):
    """Server that processes immutable requests and returns new response data"""

    def __init__(self, channel_name: str, config: BufferConfig, timeout: Optional[float] = None) -> None:
        """
        Create an immutable duplex server

        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            timeout: Optional timeout in seconds for read operations (None for default)
        """
        self._channel_name = channel_name
        self._request_buffer_name = f"{channel_name}_request"
        self._response_buffer_name = f"{channel_name}_response"
        self._config = config
        self._timeout = timeout if timeout is not None else 5.0  # Default 5 seconds

        self._request_reader: Optional[Reader] = None
        self._response_writer: Optional[Writer] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None
        self._handler: Optional[Callable[[Frame, Writer], None]] = None
        self._lock = threading.Lock()
        self._error_handlers: List[Callable[[ErrorEventArgs], None]] = []

    def _is_running(self) -> bool:
        """Check if server should keep running (thread-safe check)"""
        return bool(self._running)

    @property
    def request_reader(self) -> Optional[Reader]:
        """Get the request reader instance (for metadata access)"""
        return self._request_reader

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
                if logger:
                    logger.error(f"Error in error handler: {e}")

    def start(
        self,
        handler: Callable[[Frame, Writer], None],
        on_init: Optional[Callable[[memoryview], None]] = None,
        mode: ProcessingMode = ProcessingMode.SINGLE_THREAD,
    ) -> None:
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

    def _process_requests(self) -> None:
        """Process requests synchronously"""
        try:
            # Create request buffer as reader
            self._request_reader = Reader(self._request_buffer_name, self._config)

            # Connect to response buffer as writer (matching C# behavior)
            # The response buffer is created by the client, so we retry until it's available
            # Use the configured timeout, not a hardcoded value
            max_retries = int(self._timeout * 10)  # Convert timeout to 100ms intervals
            retry_count = 0
            while retry_count < max_retries:
                if not self._is_running():
                    return
                try:
                    self._response_writer = Writer(self._response_buffer_name)
                    break
                except Exception as e:
                    if retry_count == 0 or retry_count % 10 == 0:
                        if logger:
                            logger.debug(f"Failed to connect to response buffer (attempt {retry_count + 1}): {type(e).__name__}: {e}")
                    retry_count += 1
                    time.sleep(0.1)

            if self._response_writer is None:
                if logger:
                    logger.error(f"Failed to connect to response buffer: {self._response_buffer_name}")
                self._running = False
                return

            # Call initialization callback with metadata if provided
            if hasattr(self, "_on_init") and self._on_init and self._request_reader:
                if logger:
                    logger.debug("Reading metadata from request buffer")
                metadata = self._request_reader.get_metadata()
                if metadata:
                    if logger:
                        logger.debug(f"Got metadata: {len(metadata)} bytes")
                    try:
                        self._on_init(metadata)
                    except Exception as e:
                        if logger:
                            logger.error(f"Error in initialization callback: {e}")
                        self._invoke_error_handlers(e)
                    finally:
                        # Release the memoryview
                        metadata.release()
                else:
                    if logger:
                        logger.warning("No metadata available in request buffer")

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
                        # Process request - pass both frame and response writer like C#
                        if self._handler is None:
                            raise RuntimeError("Handler not set")
                        # Match C# signature: handler(request, responseWriter)
                        self._handler(frame, self._response_writer)

                except (ReaderDeadException, WriterDeadException) as e:
                    if logger:
                        logger.info("Client disconnected")
                    self._invoke_error_handlers(e)
                    self._running = False
                    break
                except Exception as e:
                    if logger:
                        logger.error(f"Error processing request: {e}")
                    self._invoke_error_handlers(e)
                    # Continue processing after non-fatal errors

        except Exception as e:
            if logger:
                logger.error(f"Fatal error in processing thread: {e}")
            self._invoke_error_handlers(e)
            self._running = False
        finally:
            self._cleanup()
            self._running = False

    def _cleanup(self) -> None:
        """Clean up resources"""
        if self._response_writer:
            self._response_writer.close()
            self._response_writer = None

        if self._request_reader:
            self._request_reader.close()
            self._request_reader = None

        # Clear thread reference
        self._thread = None

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
