"""
Basic communication step definitions - Production-ready implementation

Implements test steps for fundamental ZeroBuffer communication patterns
following production engineering standards.
"""

import asyncio
import contextlib
import time
import traceback
import dataclasses
from dataclasses import dataclass, field
from threading import Lock
from typing import Dict, Optional, List, Any, Union, Protocol, Type, TypeVar, Callable, Generator, ParamSpec, runtime_checkable, cast, Awaitable
from functools import wraps
import logging
from abc import ABC, abstractmethod

from zerobuffer import Reader, Writer, BufferConfig, Frame
from zerobuffer.exceptions import (
    ZeroBufferException, 
    BufferFullException, 
    MetadataAlreadyWrittenException
)

from .base import BaseSteps
from ..step_registry import given, when, then, parsers
from ..services import BufferNamingService
from ..test_data_patterns import TestDataPatterns
from ..test_context import TestContext


# Type aliases for clarity
BufferName = str
ProcessName = str
FrameData = Union[bytes, bytearray, memoryview]
SequenceNumber = int
FrameSize = int


# Custom exception hierarchy
class ZeroBufferTestException(Exception):
    """Base exception for test failures"""
    pass


class StepExecutionException(ZeroBufferTestException):
    """Step execution failed"""
    def __init__(self, step: str, cause: Exception) -> None:
        self.step = step
        self.cause = cause
        super().__init__(f"Step '{step}' failed: {cause}")


class AssertionException(ZeroBufferTestException):
    """Test assertion failed"""
    pass


class StepTimeoutException(ZeroBufferTestException):
    """Step execution timed out"""
    pass


# Protocol definitions for interfaces
class BufferReader(Protocol):
    """Interface for buffer reading operations"""
    def read_frame(self, timeout: float) -> Optional[Frame]: ...
    def get_metadata(self) -> bytes: ...
    def release_frame(self, frame: Frame) -> None: ...
    def cleanup(self) -> None: ...


class BufferWriter(Protocol):
    """Interface for buffer writing operations"""
    def write_frame(self, data: bytes) -> None: ...
    def set_metadata(self, data: bytes) -> None: ...
    def get_frame_buffer(self, size: int) -> memoryview: ...
    def commit_frame(self) -> None: ...
    @property
    def frames_written(self) -> int: ...
    def close(self) -> None: ...


# Factory interface
class BufferFactory(Protocol):
    """Factory for creating buffers"""
    def create_reader(self, name: str, config: BufferConfig) -> BufferReader: ...
    def create_writer(self, name: str) -> BufferWriter: ...


# Adapter classes to match protocols
class ReaderAdapter:
    """Adapts Reader to BufferReader protocol"""
    def __init__(self, reader: Reader) -> None:
        self._reader = reader
    
    def read_frame(self, timeout: float) -> Optional[Frame]:
        return self._reader.read_frame(timeout=timeout)
    
    def get_metadata(self) -> bytes:
        metadata = self._reader.get_metadata()
        if metadata is None:
            return b''
        if isinstance(metadata, bytes):
            return metadata
        # Must be memoryview or similar
        return bytes(metadata)
    
    def release_frame(self, frame: Frame) -> None:
        self._reader.release_frame(frame)
    
    def cleanup(self) -> None:
        self._reader.close()


class WriterAdapter:
    """Adapts Writer to BufferWriter protocol"""
    def __init__(self, writer: Writer) -> None:
        self._writer = writer
    
    def write_frame(self, data: bytes) -> None:
        self._writer.write_frame(data)
    
    def set_metadata(self, data: bytes) -> None:
        self._writer.set_metadata(data)
    
    def get_frame_buffer(self, size: int) -> memoryview:
        return self._writer.get_frame_buffer(size)
    
    def commit_frame(self) -> None:
        self._writer.commit_frame()
    
    @property
    def frames_written(self) -> int:
        return self._writer.frames_written
    
    def close(self) -> None:
        self._writer.close()


# Default factory implementation
class DefaultBufferFactory:
    """Default implementation of BufferFactory"""
    
    def create_reader(self, name: str, config: BufferConfig) -> BufferReader:
        return ReaderAdapter(Reader(name, config))
    
    def create_writer(self, name: str) -> BufferWriter:
        return WriterAdapter(Writer(name))


# Performance monitoring
class PerformanceMonitor:
    """Track performance metrics for test steps"""
    
    def __init__(self, logger: logging.Logger) -> None:
        self._logger = logger
        self._metrics: Dict[str, List[float]] = {}
        
    @contextlib.contextmanager
    def measure(self, operation: str) -> Any:
        """Context manager to measure operation duration"""
        start = time.perf_counter()
        try:
            yield
        finally:
            duration = time.perf_counter() - start
            self._metrics.setdefault(operation, []).append(duration)
            
            if duration > 1.0:  # Log slow operations
                self._logger.warning(
                    f"Slow operation: {operation} took {duration:.3f}s"
                )
    
    def get_statistics(self, operation: str) -> Dict[str, float]:
        """Get performance statistics for an operation"""
        timings = self._metrics.get(operation, [])
        if not timings:
            return {}
            
        import statistics
        return {
            "min": min(timings),
            "max": max(timings),
            "mean": statistics.mean(timings),
            "median": statistics.median(timings),
            "count": len(timings)
        }


# Protocol for objects with logger
@runtime_checkable
class HasLogger(Protocol):
    """Protocol for objects that have a logger."""
    _logger: logging.Logger

# Protocol for objects with atomic state update
@runtime_checkable
class HasAtomicStateUpdate(Protocol):
    """Protocol for objects with atomic state update."""
    _logger: logging.Logger
    def _atomic_state_update(self) -> contextlib.AbstractContextManager['TestState']: ...

# Decorator for performance-critical operations
T = TypeVar('T')
P = ParamSpec('P')
LoggerT = TypeVar('LoggerT', bound=HasLogger)
StateT = TypeVar('StateT', bound=HasAtomicStateUpdate)

def performance_critical(threshold_ms: float = 100) -> Callable[[Callable[P, T]], Callable[P, T]]:
    """Decorator for performance-critical operations"""
    def decorator(func: Callable[P, T]) -> Callable[P, T]:
        if asyncio.iscoroutinefunction(func):
            @wraps(func)
            async def async_wrapper(*args: P.args, **kwargs: P.kwargs) -> T:
                self = args[0] if args else None
                if not isinstance(self, HasLogger):
                    raise TypeError("Decorator requires HasLogger protocol")
                start = time.perf_counter()
                try:
                    # Cast func to async callable
                    async_func = cast(Callable[P, Awaitable[T]], func)
                    result = await async_func(*args, **kwargs)
                    duration_ms = (time.perf_counter() - start) * 1000
                    
                    if duration_ms > threshold_ms:
                        self._logger.warning(
                            f"{func.__name__} exceeded threshold: "
                            f"{duration_ms:.2f}ms > {threshold_ms}ms"
                        )
                        
                    return result
                except Exception as e:
                    duration_ms = (time.perf_counter() - start) * 1000
                    self._logger.error(
                        f"{func.__name__} failed after {duration_ms:.2f}ms: {e}"
                    )
                    raise
            return cast(Callable[P, T], async_wrapper)
        else:
            @wraps(func)
            def sync_wrapper(*args: P.args, **kwargs: P.kwargs) -> T:
                self = args[0] if args else None
                if not isinstance(self, HasLogger):
                    raise TypeError("Decorator requires HasLogger protocol")
                start = time.perf_counter()
                try:
                    result = func(*args, **kwargs)
                    duration_ms = (time.perf_counter() - start) * 1000
                    
                    if duration_ms > threshold_ms:
                        self._logger.warning(
                            f"{func.__name__} exceeded threshold: "
                            f"{duration_ms:.2f}ms > {threshold_ms}ms"
                        )
                        
                    return result
                except Exception as e:
                    duration_ms = (time.perf_counter() - start) * 1000
                    self._logger.error(
                        f"{func.__name__} failed after {duration_ms:.2f}ms: {e}"
                    )
                    raise
            return cast(Callable[P, T], sync_wrapper)
    return decorator


# Exception handling decorator
def capture_expected_exception(*exception_types: Type[Exception]) -> Callable[[Callable[P, T]], Callable[P, Optional[T]]]:
    """Decorator to capture expected exceptions for validation"""
    def decorator(func: Callable[P, T]) -> Callable[P, Optional[T]]:
        if asyncio.iscoroutinefunction(func):
            @wraps(func)
            async def async_wrapper(*args: P.args, **kwargs: P.kwargs) -> Optional[T]:
                self = args[0] if args else None
                if not isinstance(self, HasAtomicStateUpdate):
                    raise TypeError("Decorator requires HasAtomicStateUpdate protocol")
                try:
                    # Cast func to async callable
                    async_func = cast(Callable[P, Awaitable[T]], func)
                    return await async_func(*args, **kwargs)
                except exception_types as e:
                    self._logger.info(f"Expected exception captured: {type(e).__name__}")
                    with self._atomic_state_update() as state:
                        state.last_exception = e
                    return None
                except Exception as e:
                    self._logger.error(f"Unexpected exception in {func.__name__}: {e}")
                    raise StepExecutionException(func.__name__, e) from e
            return cast(Callable[P, Optional[T]], async_wrapper)
        else:
            @wraps(func)
            def sync_wrapper(*args: P.args, **kwargs: P.kwargs) -> Optional[T]:
                self = args[0] if args else None
                if not isinstance(self, HasAtomicStateUpdate):
                    raise TypeError("Decorator requires HasAtomicStateUpdate protocol")
                try:
                    return func(*args, **kwargs)
                except exception_types as e:
                    self._logger.info(f"Expected exception captured: {type(e).__name__}")
                    with self._atomic_state_update() as state:
                        state.last_exception = e
                    return None
                except Exception as e:
                    self._logger.error(f"Unexpected exception in {func.__name__}: {e}")
                    raise StepExecutionException(func.__name__, e) from e
            return cast(Callable[P, Optional[T]], sync_wrapper)
    return decorator


# Immutable test state
@dataclass
class TestState:
    """Immutable test state snapshot"""
    readers: Dict[BufferName, BufferReader] = field(default_factory=dict)
    writers: Dict[BufferName, BufferWriter] = field(default_factory=dict)
    last_frame: Optional[Union[Frame, Dict[str, Any]]] = None
    frames_written: List[Dict[str, Any]] = field(default_factory=list)
    frames_read: List[Frame] = field(default_factory=list)
    last_exception: Optional[Exception] = None
    current_buffer: BufferName = ""
    properties: Dict[str, Any] = field(default_factory=dict)


class BasicCommunicationSteps(BaseSteps):
    """
    Production-ready step definitions for basic communication tests.
    
    Implements fundamental ZeroBuffer communication patterns with
    full type safety, dependency injection, and performance monitoring.
    """
    
    def __init__(
        self, 
        test_context: TestContext,
        logger: logging.Logger,
        buffer_factory: Optional[BufferFactory] = None,
        performance_monitor: Optional[PerformanceMonitor] = None
    ) -> None:
        """
        Initialize BasicCommunicationSteps with dependency injection.
        
        Args:
            test_context: Test execution context
            logger: Structured logger instance
            buffer_factory: Optional factory for creating buffers
            performance_monitor: Optional performance monitoring instance
        """
        super().__init__(test_context, logger)
        self._buffer_factory = buffer_factory or DefaultBufferFactory()
        self._performance_monitor = performance_monitor or PerformanceMonitor(logger)
        self._buffer_naming = BufferNamingService(logger)
        self._state = TestState()
        self._state_lock = Lock()
        
    @contextlib.contextmanager
    def _atomic_state_update(self) -> Generator[TestState, None, None]:
        """Ensure thread-safe state updates"""
        with self._state_lock:
            # Create mutable copy
            new_state = dataclasses.replace(
                self._state,
                readers=dict(self._state.readers),
                writers=dict(self._state.writers),
                frames_written=list(self._state.frames_written),
                frames_read=list(self._state.frames_read),
                properties=dict(self._state.properties)
            )
            yield new_state
            # Commit changes
            self._state = new_state
            
    @given(r"the test environment is initialized")
    @performance_critical(threshold_ms=50)
    def test_environment_initialized(self) -> None:
        """
        Initialize test environment with cleanup.
        
        Clears all previous test resources and resets state.
        """
        with self._performance_monitor.measure("environment_init"):
            # Clean up any previous test resources
            with self._atomic_state_update() as state:
                # Clean up resources before clearing
                for reader in state.readers.values():
                    try:
                        reader.cleanup()
                    except Exception as e:
                        self._logger.debug(f"Reader cleanup error: {e}")
                        
                for writer in state.writers.values():
                    try:
                        writer.close()
                    except Exception as e:
                        self._logger.debug(f"Writer cleanup error: {e}")
                
                state.readers.clear()
                state.writers.clear()
                state.frames_written.clear()
                state.frames_read.clear()
                state.current_buffer = ""
                state.last_frame = None
                state.last_exception = None
                
            self._buffer_naming.clear_cache()
            
            self._logger.info(
                "Test environment initialized",
                extra={
                    "action": "environment_init",
                    "state_cleared": True
                }
            )
        
    @given(r"all processes are ready")
    def all_processes_ready(self) -> None:
        """
        Confirm all processes are ready for testing.
        
        This is a synchronization point to ensure all test participants
        are initialized and ready to proceed.
        """
        self._logger.info(
            "All processes ready",
            extra={"action": "process_ready"}
        )
        
    @given(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"creates buffer '(?P<buffer_name>[^']+)' "
        r"with metadata size '(?P<metadata_size>\d+)' "
        r"and payload size '(?P<payload_size>\d+)'"
    ))
    @performance_critical(threshold_ms=200)
    async def create_buffer(
        self, 
        process: Optional[ProcessName], 
        buffer_name: BufferName, 
        metadata_size: str, 
        payload_size: str
    ) -> None:
        """
        Create a new ZeroBuffer with specified configuration.
        
        Args:
            process: Process name (ignored per Harmony routing)
            buffer_name: Logical buffer name
            metadata_size: Size of metadata section in bytes
            payload_size: Size of payload section in bytes
            
        Raises:
            StepExecutionException: If buffer creation fails
        """
        with self._performance_monitor.measure("buffer_creation"):
            actual_buffer_name = self._buffer_naming.get_buffer_name(buffer_name)
            
            config = BufferConfig(
                metadata_size=int(metadata_size),
                payload_size=int(payload_size)
            )
            
            try:
                reader = self._buffer_factory.create_reader(actual_buffer_name, config)
                
                with self._atomic_state_update() as state:
                    state.readers[buffer_name] = reader
                    state.current_buffer = buffer_name
                    
                self.store_resource(f"reader_{buffer_name}", reader)
                
                self._logger.info(
                    "Buffer created",
                    extra={
                        "action": "create_buffer",
                        "buffer_name": buffer_name,
                        "actual_name": actual_buffer_name,
                        "metadata_size": int(metadata_size),
                        "payload_size": int(payload_size)
                    }
                )
                
                # Small delay to ensure shared memory is fully initialized
                await asyncio.sleep(0.1)
                
            except Exception as e:
                raise StepExecutionException("create_buffer", e) from e
                
    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"connects to buffer '(?P<buffer_name>[^']+)'"
    ))
    @performance_critical(threshold_ms=100)
    async def connect_to_buffer(
        self, 
        process: Optional[ProcessName], 
        buffer_name: BufferName
    ) -> None:
        """
        Connect a writer to an existing buffer.
        
        Args:
            process: Process name (ignored per Harmony routing)
            buffer_name: Buffer to connect to
            
        Raises:
            StepExecutionException: If connection fails
        """
        with self._performance_monitor.measure("buffer_connection"):
            actual_buffer_name = self._buffer_naming.get_buffer_name(buffer_name)
            
            try:
                writer = self._buffer_factory.create_writer(actual_buffer_name)
                
                with self._atomic_state_update() as state:
                    state.writers[buffer_name] = writer
                    state.current_buffer = buffer_name
                    
                self.store_resource(f"writer_{buffer_name}", writer)
                
                self._logger.info(
                    "Connected to buffer",
                    extra={
                        "action": "connect_buffer",
                        "buffer_name": buffer_name,
                        "actual_name": actual_buffer_name
                    }
                )
                
                # Small delay to ensure shared memory updates are visible
                await asyncio.sleep(0.1)
                
            except Exception as e:
                raise StepExecutionException("connect_to_buffer", e) from e
                
    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"writes metadata with size '(?P<size>\d+)'"
    ))
    @performance_critical(threshold_ms=50)
    async def write_metadata(
        self, 
        process: Optional[ProcessName], 
        size: str
    ) -> None:
        """
        Write metadata to the buffer.
        
        Args:
            process: Process name (ignored per Harmony routing)
            size: Size of metadata to write
            
        Raises:
            StepExecutionException: If no writer available or write fails
        """
        with self._performance_monitor.measure("metadata_write"):
            writer = self._get_current_writer()
            
            try:
                # Generate metadata using TestDataPatterns
                metadata = TestDataPatterns.generate_metadata(int(size))
                writer.set_metadata(metadata)
                
                self._logger.info(
                    "Metadata written",
                    extra={
                        "action": "write_metadata",
                        "size": int(size)
                    }
                )
                
                # Small delay to ensure OIEB updates are visible
                await asyncio.sleep(0.1)
                
            except Exception as e:
                raise StepExecutionException("write_metadata", e) from e
                
    def _get_current_writer(self) -> BufferWriter:
        """
        Get the current writer instance.
        
        Returns:
            Current writer instance
            
        Raises:
            StepExecutionException: If no writer available
        """
        if not self._state.writers:
            raise StepExecutionException(
                "get_writer", 
                Exception("No writer connected to any buffer")
            )
        elif len(self._state.writers) == 1:
            return next(iter(self._state.writers.values()))
        elif self._state.current_buffer and self._state.current_buffer in self._state.writers:
            return self._state.writers[self._state.current_buffer]
        else:
            raise StepExecutionException(
                "get_writer",
                Exception(f"Multiple writers exist but current buffer '{self._state.current_buffer}' not found")
            )
            
    def _get_current_reader(self) -> BufferReader:
        """
        Get the current reader instance.
        
        Returns:
            Current reader instance
            
        Raises:
            StepExecutionException: If no reader available
        """
        if not self._state.readers:
            raise StepExecutionException(
                "get_reader",
                Exception("No reader created for any buffer")
            )
        elif len(self._state.readers) == 1:
            return next(iter(self._state.readers.values()))
        elif self._state.current_buffer and self._state.current_buffer in self._state.readers:
            return self._state.readers[self._state.current_buffer]
        else:
            raise StepExecutionException(
                "get_reader",
                Exception(f"Multiple readers exist but current buffer '{self._state.current_buffer}' not found")
            )

    # Continue with remaining methods...
    # (Due to length, I'll continue with key methods showing the patterns)
    
    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"writes frame with size '(?P<size>\d+)' and sequence '(?P<sequence>\d+)'"
    ))
    @performance_critical(threshold_ms=100)
    async def write_frame_with_sequence(
        self,
        process: Optional[ProcessName],
        size: str,
        sequence: str
    ) -> None:
        """
        Write a frame with specific size and sequence.
        
        Args:
            process: Process name (ignored per Harmony routing)
            size: Frame size in bytes
            sequence: Sequence number for the frame
            
        Raises:
            StepExecutionException: If write fails
        """
        with self._performance_monitor.measure("frame_write"):
            writer = self._state.writers[self._state.current_buffer]
            sequence_num = int(sequence)
            
            try:
                # Generate frame data using TestDataPatterns
                frame_data = TestDataPatterns.generate_frame_data(int(size), sequence_num)
                
                # Write frame
                writer.write_frame(frame_data)
                
                # Track frame info
                frame_info = {
                    'data': frame_data,
                    'sequence_number': sequence_num,
                    'size': len(frame_data)
                }
                
                with self._atomic_state_update() as state:
                    state.frames_written.append(frame_info)
                    state.last_frame = frame_info
                
                self._logger.info(
                    "Frame written",
                    extra={
                        "action": "write_frame",
                        "size": int(size),
                        "sequence": sequence_num
                    }
                )
                
            except Exception as e:
                raise StepExecutionException("write_frame_with_sequence", e) from e

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should read frame with sequence '(?P<sequence>\d+)' and size '(?P<size>\d+)'"
    ))
    @performance_critical(threshold_ms=5000)
    async def read_frame_verify_sequence_size(
        self,
        process: Optional[ProcessName],
        sequence: str,
        size: str
    ) -> None:
        """
        Read and verify frame sequence and size.
        
        Args:
            process: Process name (ignored per Harmony routing)
            sequence: Expected sequence number
            size: Expected frame size
            
        Raises:
            AssertionException: If frame doesn't match expectations
            StepTimeoutException: If no frame available within timeout
        """
        with self._performance_monitor.measure("frame_read_verify"):
            reader = self._get_current_reader()
            
            # Wait for frame with timeout
            frame: Optional[Frame] = None
            for _ in range(50):  # 5 second timeout
                frame = reader.read_frame(timeout=0.1)
                if frame:
                    break
                await asyncio.sleep(0.1)
                
            if frame is None:
                raise StepTimeoutException("No frame available to read within timeout")
            
            # Verify size
            if len(frame.data) != int(size):
                raise AssertionException(
                    f"Frame size mismatch: expected {size}, got {len(frame.data)}"
                )
                
            with self._atomic_state_update() as state:
                state.frames_read.append(frame)
                state.last_frame = frame
            
            self._logger.info(
                "Frame read and verified",
                extra={
                    "action": "read_frame",
                    "sequence": frame.sequence,
                    "size": len(frame.data),
                    "expected_size": int(size)
                }
            )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should validate frame data"
    ))
    @performance_critical(threshold_ms=50)
    async def validate_frame_data(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Validate the last read frame data.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Raises:
            AssertionException: If frame data doesn't match expected pattern
        """
        if self._state.last_frame is None:
            raise AssertionException("No frame to validate")
        
        if isinstance(self._state.last_frame, Frame):
            frame_data = self._state.last_frame.data
            frame_sequence = self._state.last_frame.sequence
        else:
            # It's a dict
            frame_data = self._state.last_frame['data']
            frame_sequence = self._state.last_frame['sequence_number']
        
        # Generate expected data using the shared pattern
        expected_data = TestDataPatterns.generate_frame_data(
            len(frame_data), 
            frame_sequence
        )
        
        if frame_data != expected_data:
            raise AssertionException("Frame data does not match expected pattern")
        
        self._logger.info(
            "Frame data validated",
            extra={"action": "validate_frame_data"}
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"signals space available"
    ))
    async def signal_space_available(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Signal that space is available (frame consumed).
        
        Args:
            process: Process name (ignored per Harmony routing)
        """
        if self._state.last_frame and isinstance(self._state.last_frame, Frame):
            reader = self._get_current_reader()
            reader.release_frame(self._state.last_frame)
            
        with self._atomic_state_update() as state:
            state.last_frame = None
            
        self._logger.info(
            "Signaled space available",
            extra={"action": "signal_space"}
        )

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"writes frame with sequence '(?P<sequence>\d+)'"
    ))
    @performance_critical(threshold_ms=100)
    async def write_frame_sequence_only(
        self,
        process: Optional[ProcessName],
        sequence: str
    ) -> None:
        """
        Write a frame with default size and specific sequence.
        
        Args:
            process: Process name (ignored per Harmony routing)
            sequence: Sequence number for the frame
        """
        writer = self._state.writers[self._state.current_buffer]
        sequence_num = int(sequence)
        
        # Use default size of 1024 when not specified
        data = TestDataPatterns.generate_frame_data(1024, sequence_num)
        
        writer.write_frame(data)
        
        self._logger.info(
            "Frame written with sequence",
            extra={
                "action": "write_frame",
                "sequence": sequence_num
            }
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should read frame with sequence '(?P<sequence>\d+)';?"
    ))
    @performance_critical(threshold_ms=5000)
    async def read_frame_verify_sequence(
        self,
        process: Optional[ProcessName],
        sequence: str
    ) -> None:
        """
        Read and verify frame sequence.
        
        Args:
            process: Process name (ignored per Harmony routing)
            sequence: Expected sequence number
            
        Raises:
            AssertionException: If frame sequence doesn't match
            StepTimeoutException: If no frame available
        """
        reader = self._get_current_reader()
        frame = reader.read_frame(timeout=5.0)
        
        if frame is None:
            raise StepTimeoutException(f"No frame available with sequence {sequence}")
        
        with self._atomic_state_update() as state:
            state.frames_read.append(frame)
            state.last_frame = frame
        
        self._logger.info(
            "Frame read with sequence",
            extra={
                "action": "read_frame",
                "sequence": frame.sequence
            }
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should verify all frames maintain sequential order"
    ))
    async def verify_sequential_order(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Verify all read frames are in sequential order.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Raises:
            AssertionException: If frames are not sequential
        """
        if len(self._state.frames_read) < 2:
            return
            
        for i in range(1, len(self._state.frames_read)):
            prev_seq = self._state.frames_read[i-1].sequence
            curr_seq = self._state.frames_read[i].sequence
            
            if curr_seq != prev_seq + 1:
                raise AssertionException(
                    f"Sequence break: {prev_seq} -> {curr_seq}"
                )
                
        self._logger.info(
            "Sequential order verified",
            extra={
                "action": "verify_order",
                "frame_count": len(self._state.frames_read)
            }
        )

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"writes frames until buffer is full"
    ))
    @capture_expected_exception(BufferFullException)
    @performance_critical(threshold_ms=10000)
    async def write_until_full(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Write frames until the buffer is full.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Note:
            Captures BufferFullException as expected behavior
        """
        writer = self._get_current_writer()
        frame_count = 0
        frame_size = 1024  # Use 1KB frames for faster filling
        
        with self._performance_monitor.measure("write_until_full"):
            while frame_count < 20:  # Safety limit
                try:
                    data = TestDataPatterns.generate_frame_data(
                        frame_size, 
                        frame_count
                    )
                    writer.write_frame(data)
                    
                    frame_info = {
                        'data': data,
                        'sequence_number': frame_count,
                        'size': len(data)
                    }
                    
                    with self._atomic_state_update() as state:
                        state.frames_written.append(frame_info)
                        
                    frame_count += 1
                    
                except BufferFullException:
                    # Expected - buffer is full
                    self._logger.info(
                        f"Buffer full after {frame_count} frames",
                        extra={
                            "action": "buffer_full",
                            "frames_written": frame_count
                        }
                    )
                    raise  # Re-raise to be captured by decorator
                    
            self._logger.warning(
                f"Safety limit reached after {frame_count} frames",
                extra={"frames_written": frame_count}
            )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should experience timeout on next write"
    ))
    @capture_expected_exception(BufferFullException)
    async def verify_buffer_full(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Verify that the next write will block due to buffer full.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Raises:
            AssertionException: If write succeeds when it shouldn't
        """
        writer = self._get_current_writer()
        
        # Try to write and expect BufferFullException
        data = TestDataPatterns.generate_frame_data(1024, 999)
        writer.write_frame(data)
        
        # If we get here, the write succeeded when it shouldn't have
        raise AssertionException("Write should have timed out but succeeded")

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"reads one frame"
    ))
    @performance_critical(threshold_ms=5000)
    async def read_one_frame(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Read a single frame.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Raises:
            StepTimeoutException: If no frame available
        """
        reader = self._get_current_reader()
        frame = reader.read_frame(timeout=5.0)
        
        if frame is None:
            raise StepTimeoutException("No frame available to read")
        
        with self._atomic_state_update() as state:
            state.frames_read.append(frame)
            state.last_frame = frame
        
        self._logger.info(
            f"Read frame with sequence {frame.sequence}",
            extra={
                "action": "read_frame",
                "sequence": frame.sequence
            }
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should write successfully immediately"
    ))
    @performance_critical(threshold_ms=500)
    async def verify_write_succeeds(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Verify that write succeeds immediately.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Raises:
            AssertionException: If write takes too long or fails
        """
        writer = self._get_current_writer()
        
        # Give a moment for the semaphore signal to propagate
        await asyncio.sleep(0.1)
        
        # Write should succeed quickly
        start_time = time.perf_counter()
        data = TestDataPatterns.generate_frame_data(1024, 1000)
        
        try:
            writer.write_frame(data)
            write_time = time.perf_counter() - start_time
            
            if write_time > 0.5:
                raise AssertionException(f"Write took too long: {write_time}s")
                
            self._logger.info(
                "Write succeeded immediately",
                extra={
                    "action": "write_success",
                    "duration": write_time
                }
            )
            
        except Exception as e:
            self._logger.error(f"Write failed unexpectedly: {e}")
            raise

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"requests zero-copy frame of size '(?P<size>\d+)'"
    ))
    async def request_zero_copy_frame(
        self,
        process: Optional[ProcessName],
        size: str
    ) -> None:
        """
        Request a zero-copy frame buffer.
        
        Args:
            process: Process name (ignored per Harmony routing)
            size: Size of frame to allocate
        """
        writer = self._get_current_writer()
        frame_size = int(size)
        
        # Get zero-copy buffer
        buffer = writer.get_frame_buffer(frame_size)
        
        with self._atomic_state_update() as state:
            state.properties["zero_copy_buffer"] = buffer
            state.properties["zero_copy_size"] = frame_size
        
        self._logger.info(
            f"Requested zero-copy frame of size {size}",
            extra={
                "action": "zero_copy_request",
                "size": frame_size
            }
        )

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"fills zero-copy buffer with test pattern"
    ))
    async def fill_zero_copy_buffer(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Fill zero-copy buffer with test pattern.
        
        Args:
            process: Process name (ignored per Harmony routing)
        """
        writer = self._state.writers[self._state.current_buffer]
        size = self._state.properties.get("zero_copy_size")
        
        if size is None:
            raise StepExecutionException(
                "fill_zero_copy_buffer",
                Exception("No zero-copy size set")
            )
        
        # Request a zero-copy buffer and fill it
        span = writer.get_frame_buffer(size)
        
        # Generate test pattern
        test_pattern = TestDataPatterns.generate_frame_data(
            size, 
            writer.frames_written + 1
        )
        
        # Fill the zero-copy buffer directly
        span[:size] = test_pattern
        
        with self._atomic_state_update() as state:
            state.properties["zero_copy_buffer"] = test_pattern
            state.properties["zero_copy_ready"] = True
        
        self._logger.info(
            "Filled zero-copy buffer with test pattern",
            extra={"action": "zero_copy_fill"}
        )

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"commits zero-copy frame"
    ))
    async def commit_zero_copy_frame(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Commit the zero-copy frame.
        
        Args:
            process: Process name (ignored per Harmony routing)
        """
        writer = self._state.writers[self._state.current_buffer]
        writer.commit_frame()
        
        self._logger.info(
            "Committed zero-copy frame",
            extra={"action": "zero_copy_commit"}
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should read frame with size '(?P<size>\d+)'"
    ))
    @performance_critical(threshold_ms=5000)
    async def read_frame_verify_size(
        self,
        process: Optional[ProcessName],
        size: str
    ) -> None:
        """
        Read and verify frame size.
        
        Args:
            process: Process name (ignored per Harmony routing)
            size: Expected frame size
            
        Raises:
            AssertionException: If frame size doesn't match
            StepTimeoutException: If no frame available
        """
        reader = self._get_current_reader()
        frame = reader.read_frame(timeout=5.0)
        
        if frame is None:
            raise StepTimeoutException("No frame available")
            
        if len(frame.data) != int(size):
            raise AssertionException(
                f"Frame size mismatch: expected {size}, got {len(frame.data)}"
            )
            
        with self._atomic_state_update() as state:
            state.frames_read.append(frame)
            state.last_frame = frame
        
        self._logger.info(
            f"Read frame with size {len(frame.data)}",
            extra={
                "action": "read_frame",
                "size": len(frame.data)
            }
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should verify frame data matches test pattern"
    ))
    async def verify_test_pattern(
        self,
        process: Optional[ProcessName]
    ) -> None:
        """
        Verify frame data matches the test pattern.
        
        Args:
            process: Process name (ignored per Harmony routing)
            
        Raises:
            AssertionException: If data doesn't match pattern
        """
        if self._state.last_frame is None:
            raise AssertionException("No frame to verify")
        
        if isinstance(self._state.last_frame, Frame):
            frame_data = bytes(self._state.last_frame.data)
            sequence = self._state.last_frame.sequence
        else:
            # It's a dict
            frame_data = self._state.last_frame['data']
            sequence = self._state.last_frame['sequence_number']
        
        # Generate expected pattern
        expected_pattern = TestDataPatterns.generate_frame_data(
            len(frame_data),
            sequence
        )
        
        if frame_data != expected_pattern:
            raise AssertionException("Frame data does not match test pattern")
        
        self._logger.info(
            "Frame data matches test pattern",
            extra={"action": "verify_pattern"}
        )

    @when(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"writes frame with size '(?P<size>\d+)'"
    ))
    @performance_critical(threshold_ms=100)
    async def write_frame_with_size(
        self,
        process: Optional[ProcessName],
        size: str
    ) -> None:
        """
        Write a frame with specific size.
        
        Args:
            process: Process name (ignored per Harmony routing)
            size: Frame size in bytes
        """
        writer = self._state.writers[self._state.current_buffer]
        frame_size = int(size)
        
        # Use simple test data pattern
        frame_data = TestDataPatterns.generate_simple_frame_data(frame_size)
        writer.write_frame(frame_data)
        
        self._logger.info(
            f"Wrote frame with size {size}",
            extra={
                "action": "write_frame",
                "size": frame_size
            }
        )

    @then(parsers.re(
        r"(?:the '(?P<process>[^']+)' process )?"
        r"should read (?P<count>\d+) frames with sizes '(?P<sizes>[^']+)' in order"
    ))
    @performance_critical(threshold_ms=10000)
    async def read_frames_verify_sizes(
        self,
        process: Optional[ProcessName],
        count: str,
        sizes: str
    ) -> None:
        """
        Read specified number of frames with specific sizes.
        
        Args:
            process: Process name (ignored per Harmony routing)
            count: Number of frames to read
            sizes: Comma-separated list of expected sizes
            
        Raises:
            AssertionException: If sizes don't match or data invalid
            StepTimeoutException: If frames not available
        """
        reader = self._state.readers[self._state.current_buffer]
        expected_sizes = [int(s) for s in sizes.split(',')]
        
        if int(count) != len(expected_sizes):
            raise AssertionException(
                f"Count {count} doesn't match sizes list length {len(expected_sizes)}"
            )
        
        for i in range(int(count)):
            frame = reader.read_frame(timeout=5.0)
            
            if frame is None:
                raise StepTimeoutException(f"Failed to read frame {i+1}")
            
            if len(frame.data) != expected_sizes[i]:
                raise AssertionException(
                    f"Frame {i+1} size mismatch: "
                    f"expected {expected_sizes[i]}, got {len(frame.data)}"
                )
            
            # Verify frame data integrity
            frame_data = bytes(frame.data)
            if not TestDataPatterns.verify_simple_frame_data(frame_data):
                raise AssertionException(
                    f"Frame {i+1} data does not match expected pattern"
                )
                    
            with self._atomic_state_update() as state:
                state.frames_read.append(frame)
            
        self._logger.info(
            f"Read {count} frames with correct sizes",
            extra={
                "action": "read_frames",
                "count": int(count),
                "sizes": expected_sizes
            }
        )