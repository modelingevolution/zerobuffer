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

    def __init__(self, message: str = "Writer process is dead") -> None:
        super().__init__(message)


class ReaderDeadException(ZeroBufferException):
    """Raised when the reader process is detected as dead"""

    def __init__(self, message: str = "Reader process is dead") -> None:
        super().__init__(message)


class WriterAlreadyConnectedException(ZeroBufferException):
    """Raised when attempting to connect a second writer"""

    def __init__(self, message: str = "Another writer is already connected") -> None:
        super().__init__(message)


class ReaderAlreadyConnectedException(ZeroBufferException):
    """Raised when attempting to connect a second reader"""

    def __init__(self, message: str = "Another reader is already connected") -> None:
        super().__init__(message)


class BufferFullException(ZeroBufferException):
    """Raised when the buffer is full and cannot accept more data"""

    def __init__(self, message: str = "Buffer is full") -> None:
        super().__init__(message)


class FrameTooLargeException(ZeroBufferException):
    """Raised when attempting to write a frame larger than buffer capacity"""

    def __init__(self, message: str = "Frame size exceeds buffer capacity") -> None:
        super().__init__(message)


class InvalidFrameSizeException(ZeroBufferException):
    """Raised when frame size is invalid (zero or too large)"""

    def __init__(self, message: str = "Invalid frame size (zero or too large)") -> None:
        super().__init__(message)


class SequenceError(ZeroBufferException):
    """Raised when sequence number validation fails"""

    def __init__(self, expected: int, got: int) -> None:
        super().__init__(f"Sequence error: expected {expected}, got {got}")
        self.expected = expected
        self.got = got


class MetadataAlreadyWrittenException(ZeroBufferException):
    """Raised when attempting to write metadata more than once"""

    def __init__(self, message: str = "Metadata has already been written") -> None:
        super().__init__(message)
