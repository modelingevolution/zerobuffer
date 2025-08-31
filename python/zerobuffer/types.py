"""
Core data structures for ZeroBuffer protocol

This module defines the fundamental types used in the ZeroBuffer protocol,
ensuring binary compatibility with C++ and C# implementations.
"""

import struct
from dataclasses import dataclass
from typing import Optional, Tuple, Callable, Type
from types import TracebackType

# Constants
BLOCK_ALIGNMENT = 64


@dataclass
class ProtocolVersion:
    """Protocol version structure (4 bytes)"""

    major: int  # Major version (breaking changes)
    minor: int  # Minor version (new features, backward compatible)
    patch: int  # Patch version (bug fixes)
    reserved: int  # Reserved for future use (must be 0)

    FORMAT = "<4B"  # 4 unsigned bytes
    SIZE = 4

    def pack(self) -> bytes:
        """Pack version into bytes"""
        return struct.pack(self.FORMAT, self.major, self.minor, self.patch, self.reserved)

    @classmethod
    def unpack(cls, data: bytes) -> "ProtocolVersion":
        """Unpack version from bytes"""
        values = struct.unpack(cls.FORMAT, data[: cls.SIZE])
        return cls(major=values[0], minor=values[1], patch=values[2], reserved=values[3])


@dataclass
class OIEB:
    """
    Operation Info Exchange Block

    Must match the C++ OIEB structure exactly for cross-language compatibility.
    """

    oieb_size: int  # Total OIEB size (uint32_t, always 128 for v1.x.x)
    version: ProtocolVersion  # Protocol version (4 bytes)
    metadata_size: int  # Total metadata block size (uint64_t)
    metadata_free_bytes: int  # Free bytes in metadata block
    metadata_written_bytes: int  # Written bytes in metadata block
    payload_size: int  # Total payload block size
    payload_free_bytes: int  # Free bytes in payload block
    payload_write_pos: int  # Current write position in buffer
    payload_read_pos: int  # Current read position in buffer
    payload_written_count: int  # Number of frames written
    payload_read_count: int  # Number of frames read
    writer_pid: int  # Writer process ID (0 if none)
    reader_pid: int  # Reader process ID (0 if none)
    _reserved: Tuple[int, int, int, int] = (0, 0, 0, 0)  # Padding for 128-byte size

    # Binary format: uint32 + 4 bytes version + 11 uint64 + 4 uint64 reserved
    FORMAT = "<I4B11Q4Q"  # I=uint32, B=uint8, Q=uint64
    SIZE = 128  # Always 128 bytes

    def pack(self) -> bytes:
        """Pack OIEB into bytes for writing to shared memory"""
        return struct.pack(
            self.FORMAT,
            self.oieb_size,
            self.version.major,
            self.version.minor,
            self.version.patch,
            self.version.reserved,
            self.metadata_size,
            self.metadata_free_bytes,
            self.metadata_written_bytes,
            self.payload_size,
            self.payload_free_bytes,
            self.payload_write_pos,
            self.payload_read_pos,
            self.payload_written_count,
            self.payload_read_count,
            self.writer_pid,
            self.reader_pid,
            *self._reserved,
        )

    @classmethod
    def unpack(cls, data: bytes) -> "OIEB":
        """Unpack OIEB from bytes read from shared memory"""
        if len(data) < cls.SIZE:
            raise ValueError(f"Invalid OIEB data size: {len(data)} < {cls.SIZE}")

        values = struct.unpack(cls.FORMAT, data[: cls.SIZE])
        return cls(
            oieb_size=values[0],
            version=ProtocolVersion(major=values[1], minor=values[2], patch=values[3], reserved=values[4]),
            metadata_size=values[5],
            metadata_free_bytes=values[6],
            metadata_written_bytes=values[7],
            payload_size=values[8],
            payload_free_bytes=values[9],
            payload_write_pos=values[10],
            payload_read_pos=values[11],
            payload_written_count=values[12],
            payload_read_count=values[13],
            writer_pid=values[14],
            reader_pid=values[15],
            _reserved=values[16:20],
        )

    def calculate_used_bytes(self) -> int:
        """Calculate used bytes in the buffer"""
        if self.payload_write_pos >= self.payload_read_pos:
            return self.payload_write_pos - self.payload_read_pos
        else:
            return self.payload_size - self.payload_read_pos + self.payload_write_pos


@dataclass
class FrameHeader:
    """
    Frame header structure

    Each frame in the payload buffer is prefixed with this header.
    """

    payload_size: int  # Size of the frame data
    sequence_number: int  # Sequence number

    FORMAT = "<2Q"  # 2 unsigned 64-bit integers, little-endian
    SIZE = struct.calcsize(FORMAT)
    WRAP_MARKER = 0  # Special value indicating wrap-around

    def pack(self) -> bytes:
        """Pack header into bytes"""
        return struct.pack(self.FORMAT, self.payload_size, self.sequence_number)

    @classmethod
    def unpack(cls, data: bytes) -> "FrameHeader":
        """Unpack header from bytes"""
        if len(data) < cls.SIZE:
            raise ValueError(f"Invalid frame header size: {len(data)} < {cls.SIZE}")

        values = struct.unpack(cls.FORMAT, data[: cls.SIZE])
        return cls(payload_size=values[0], sequence_number=values[1])

    def is_wrap_marker(self) -> bool:
        """Check if this is a wrap-around marker"""
        return self.payload_size == self.WRAP_MARKER


@dataclass
class BufferConfig:
    """Configuration for creating a buffer"""

    metadata_size: int = 1024
    payload_size: int = 1024 * 1024  # 1MB default

    def __post_init__(self) -> None:
        """Validate configuration"""
        if self.metadata_size <= 0:
            raise ValueError("metadata_size must be positive")
        if self.payload_size <= 0:
            raise ValueError("payload_size must be positive")


class Frame:
    """
    Zero-copy frame reference with RAII support

    This class provides access to frame data without copying it from shared memory.
    The data is accessed through a memoryview, ensuring zero-copy operation.

    Supports context manager protocol for RAII-style resource management.
    When used with 'with' statement, the disposal callback is called on exit.
    """

    def __init__(
        self,
        memory_view: memoryview,
        offset: int,
        size: int,
        sequence: int,
        on_dispose: Optional[Callable[[], None]] = None,
    ):
        """
        Initialize frame reference

        Args:
            memory_view: Memoryview of the payload buffer
            offset: Offset of frame data within the buffer
            size: Size of frame data
            sequence: Sequence number
            on_dispose: Optional callback to call when frame is disposed
        """
        self._memory_view: Optional[memoryview] = memory_view
        self._offset = offset
        self._size = size
        self._sequence = sequence
        self._data_view: Optional[memoryview] = None
        self._on_dispose = on_dispose
        self._disposed = False

    @property
    def data(self) -> memoryview:
        """Get zero-copy view of frame data"""
        if self._disposed:
            raise ValueError("Cannot access data of disposed frame")
        if self._data_view is None and self._memory_view is not None:
            self._data_view = self._memory_view[self._offset : self._offset + self._size]
        if self._data_view is None:
            raise ValueError("Frame has no data")
        return self._data_view

    @property
    def size(self) -> int:
        """Get frame size"""
        return self._size

    @property
    def sequence(self) -> int:
        """Get sequence number"""
        return self._sequence

    @property
    def is_valid(self) -> bool:
        """Check if frame is valid (has data)"""
        return self._size > 0 and self._memory_view is not None

    def __len__(self) -> int:
        """Get frame size"""
        return self._size

    def dispose(self) -> None:
        """
        Dispose the frame and call the disposal callback.
        This is called automatically when exiting a 'with' statement.
        """
        if not self._disposed:
            self._disposed = True
            if self._on_dispose:
                self._on_dispose()
            # Properly release memoryview references
            if self._data_view is not None:
                self._data_view.release()
                self._data_view = None
            if self._memory_view is not None:
                self._memory_view.release()
                self._memory_view = None

    def __enter__(self) -> "Frame":
        """Enter context manager"""
        return self

    def __exit__(
        self, exc_type: Optional[Type[BaseException]], exc_val: Optional[BaseException], exc_tb: Optional[TracebackType]
    ) -> None:
        """Exit context manager - dispose the frame"""
        self.dispose()

    def __del__(self) -> None:
        """Destructor - ensure disposal if not already done"""
        self.dispose()

    def __bytes__(self) -> bytes:
        """Convert to bytes (creates a copy)"""
        return bytes(self.data)

    def __repr__(self) -> str:
        return f"Frame(sequence={self._sequence}, size={self._size})"


def align_to_boundary(size: int, alignment: int = BLOCK_ALIGNMENT) -> int:
    """Align size to specified boundary"""
    return ((size + alignment - 1) // alignment) * alignment
