"""
ZeroBuffer - High-performance zero-copy inter-process communication

A Python implementation of the ZeroBuffer protocol for efficient IPC
with true zero-copy data access.
"""

__version__ = "0.1.0"

from .reader import Reader
from .writer import Writer
from .types import BufferConfig, Frame, OIEB, FrameHeader
from .exceptions import (
    ZeroBufferException,
    WriterDeadException,
    ReaderDeadException,
    WriterAlreadyConnectedException,
    BufferFullException,
    FrameTooLargeException,
    SequenceError,
    InvalidFrameSizeException,
    MetadataAlreadyWrittenException
)
from .logging_config import setup_logging, get_logger

# Duplex channel support
from .duplex import (
    DuplexChannelFactory,
    DuplexClient,
    ImmutableDuplexServer,
    MutableDuplexServer,
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
    'BufferFullException',
    'FrameTooLargeException',
    'SequenceError',
    'InvalidFrameSizeException',
    'MetadataAlreadyWrittenException',
    # Logging
    'setup_logging',
    'get_logger',
    # Duplex channel
    'DuplexChannelFactory',
    'DuplexClient',
    'ImmutableDuplexServer',
    'MutableDuplexServer',
    'IDuplexClient',
    'IDuplexServer',
    'IImmutableDuplexServer',
    'IMutableDuplexServer',
    'IDuplexChannelFactory',
    'DuplexResponse',
    'ProcessingMode'
]