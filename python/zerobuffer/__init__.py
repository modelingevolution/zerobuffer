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

__all__ = [
    'Reader',
    'Writer', 
    'BufferConfig',
    'Frame',
    'ZeroBufferException',
    'WriterDeadException',
    'ReaderDeadException',
    'WriterAlreadyConnectedException',
    'BufferFullException',
    'FrameTooLargeException',
    'SequenceError',
    'InvalidFrameSizeException',
    'MetadataAlreadyWrittenException'
]