"""
ZeroBuffer - High-performance zero-copy inter-process communication

A Python implementation of the ZeroBuffer protocol for efficient IPC
with true zero-copy data access.
"""

__version__ = "1.1.8"

from .reader import Reader
from .writer import Writer
from .types import BufferConfig, Frame, OIEB, FrameHeader
from .exceptions import (
    ZeroBufferException,
    WriterDeadException,
    ReaderDeadException,
    WriterAlreadyConnectedException,
    ReaderAlreadyConnectedException,
    BufferFullException,
    FrameTooLargeException,
    SequenceError,
    InvalidFrameSizeException,
    MetadataAlreadyWrittenException
)
from .error_event_args import ErrorEventArgs
from .logging_config import setup_logging, get_logger
from .logger_factory import ILoggerFactory, LoggerFactory, NullLoggerFactory, get_default_factory, set_default_factory

# Duplex channel support
from .duplex import (
    DuplexChannelFactory,
    DuplexClient,
    ImmutableDuplexServer,
    IDuplexClient,
    IDuplexServer,
    IImmutableDuplexServer,
    IMutableDuplexServer,
    IDuplexChannelFactory,
    DuplexResponse,
    ProcessingMode
)

__all__ = [
    # Core classes
    'Reader',
    'Writer', 
    'BufferConfig',
    'Frame',
    # Exceptions
    'ZeroBufferException',
    'WriterDeadException',
    'ReaderDeadException',
    'WriterAlreadyConnectedException',
    'ReaderAlreadyConnectedException',
    'BufferFullException',
    'FrameTooLargeException',
    'SequenceError',
    'InvalidFrameSizeException',
    'MetadataAlreadyWrittenException',
    'ErrorEventArgs',
    # Logging
    'setup_logging',
    'get_logger',
    'ILoggerFactory',
    'LoggerFactory',
    'NullLoggerFactory',
    'get_default_factory',
    'set_default_factory',
    # Duplex channel
    'DuplexChannelFactory',
    'DuplexClient',
    'ImmutableDuplexServer',
    'IDuplexClient',
    'IDuplexServer',
    'IImmutableDuplexServer',
    'IMutableDuplexServer',
    'IDuplexChannelFactory',
    'DuplexResponse',
    'ProcessingMode'
]