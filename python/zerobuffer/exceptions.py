"""
Exception types for ZeroBuffer

Defines all custom exceptions used in the ZeroBuffer implementation,
matching the exceptions in C++ and C# implementations.
"""


class ZeroBufferException(Exception):
    """Base exception for all ZeroBuffer errors"""
    pass


class WriterDeadException(ZeroBufferException):
    """Raised when the writer process is detected as dead"""
    def __init__(self) -> None:
        super().__init__("Writer process is dead")


class ReaderDeadException(ZeroBufferException):
    """Raised when the reader process is detected as dead"""
    def __init__(self) -> None:
        super().__init__("Reader process is dead")


class WriterAlreadyConnectedException(ZeroBufferException):
    """Raised when attempting to connect a second writer"""
    def __init__(self) -> None:
        super().__init__("Another writer is already connected")


class BufferFullException(ZeroBufferException):
    """Raised when the buffer is full and cannot accept more data"""
    def __init__(self) -> None:
        super().__init__("Buffer is full")


class FrameTooLargeException(ZeroBufferException):
    """Raised when attempting to write a frame larger than buffer capacity"""
    def __init__(self) -> None:
        super().__init__("Frame size exceeds buffer capacity")


class InvalidFrameSizeException(ZeroBufferException):
    """Raised when frame size is invalid (zero or too large)"""
    def __init__(self) -> None:
        super().__init__("Invalid frame size (zero or too large)")


class SequenceError(ZeroBufferException):
    """Raised when sequence number validation fails"""
    def __init__(self, expected: int, got: int) -> None:
        super().__init__(f"Sequence error: expected {expected}, got {got}")
        self.expected = expected
        self.got = got


class MetadataAlreadyWrittenException(ZeroBufferException):
    """Raised when attempting to write metadata more than once"""
    def __init__(self) -> None:
        super().__init__("Metadata has already been written")