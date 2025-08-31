"""
ZeroBuffer - High-performance zero-copy inter-process communication

A Python implementation of the ZeroBuffer protocol for efficient IPC
with true zero-copy data access.
"""

__version__ = "1.1.8"

import logging
import os

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
    MetadataAlreadyWrittenException,
)
from .error_event_args import ErrorEventArgs

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
    ProcessingMode,
)

# Configure library logger with NullHandler (best practice for libraries)
logging.getLogger(__name__).addHandler(logging.NullHandler())

# Optional: Configure from environment variable
_log_level = os.environ.get("ZEROBUFFER_LOG_LEVEL")
if _log_level:
    try:
        logging.getLogger(__name__).setLevel(getattr(logging, _log_level.upper()))
    except AttributeError:
        pass  # Invalid log level, ignore

__all__ = [
    # Core classes
    "Reader",
    "Writer",
    "BufferConfig",
    "Frame",
    "OIEB",
    "FrameHeader",
    # Exceptions
    "ZeroBufferException",
    "WriterDeadException",
    "ReaderDeadException",
    "WriterAlreadyConnectedException",
    "ReaderAlreadyConnectedException",
    "BufferFullException",
    "FrameTooLargeException",
    "SequenceError",
    "InvalidFrameSizeException",
    "MetadataAlreadyWrittenException",
    "ErrorEventArgs",
    # Duplex channel
    "DuplexChannelFactory",
    "DuplexClient",
    "ImmutableDuplexServer",
    "IDuplexClient",
    "IDuplexServer",
    "IImmutableDuplexServer",
    "IMutableDuplexServer",
    "IDuplexChannelFactory",
    "DuplexResponse",
    "ProcessingMode",
]
