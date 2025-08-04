"""
Core data structures for ZeroBuffer protocol

This module defines the fundamental types used in the ZeroBuffer protocol,
ensuring binary compatibility with C++ and C# implementations.
"""

import struct
from dataclasses import dataclass
from typing import Optional, Tuple

# Constants
BLOCK_ALIGNMENT = 64


@dataclass
class OIEB:
    """
    Operation Info Exchange Block
    
    Must match the C++ OIEB structure exactly for cross-language compatibility.
    All fields are 64-bit unsigned integers.
    """
    operation_size: int          # Total OIEB size
    metadata_size: int           # Total metadata block size  
    metadata_free_bytes: int     # Free bytes in metadata block
    metadata_written_bytes: int  # Written bytes in metadata block
    payload_size: int            # Total payload block size
    payload_free_bytes: int      # Free bytes in payload block
    payload_write_pos: int       # Current write position in buffer
    payload_read_pos: int        # Current read position in buffer
    payload_written_count: int   # Number of frames written
    payload_read_count: int      # Number of frames read
    writer_pid: int              # Writer process ID (0 if none)
    reader_pid: int              # Reader process ID (0 if none)
    _reserved: Tuple[int, int, int, int] = (0, 0, 0, 0)  # Padding for 128-byte size
    
    # Binary format: 16 unsigned 64-bit integers, little-endian
    FORMAT = '<16Q'
    SIZE = struct.calcsize(FORMAT)
    
    def pack(self) -> bytes:
        """Pack OIEB into bytes for writing to shared memory"""
        return struct.pack(
            self.FORMAT,
            self.operation_size,
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
            *self._reserved
        )
    
    @classmethod
    def unpack(cls, data: bytes) -> 'OIEB':
        """Unpack OIEB from bytes read from shared memory"""
        if len(data) < cls.SIZE:
            raise ValueError(f"Invalid OIEB data size: {len(data)} < {cls.SIZE}")
        
        values = struct.unpack(cls.FORMAT, data[:cls.SIZE])
        return cls(
            operation_size=values[0],
            metadata_size=values[1],
            metadata_free_bytes=values[2],
            metadata_written_bytes=values[3],
            payload_size=values[4],
            payload_free_bytes=values[5],
            payload_write_pos=values[6],
            payload_read_pos=values[7],
            payload_written_count=values[8],
            payload_read_count=values[9],
            writer_pid=values[10],
            reader_pid=values[11],
            _reserved=values[12:16]
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
    payload_size: int      # Size of the frame data
    sequence_number: int   # Sequence number
    
    FORMAT = '<2Q'  # 2 unsigned 64-bit integers, little-endian
    SIZE = struct.calcsize(FORMAT)
    WRAP_MARKER = 0  # Special value indicating wrap-around
    
    def pack(self) -> bytes:
        """Pack header into bytes"""
        return struct.pack(self.FORMAT, self.payload_size, self.sequence_number)
    
    @classmethod
    def unpack(cls, data: bytes) -> 'FrameHeader':
        """Unpack header from bytes"""
        if len(data) < cls.SIZE:
            raise ValueError(f"Invalid frame header size: {len(data)} < {cls.SIZE}")
        
        values = struct.unpack(cls.FORMAT, data[:cls.SIZE])
        return cls(payload_size=values[0], sequence_number=values[1])
    
    def is_wrap_marker(self) -> bool:
        """Check if this is a wrap-around marker"""
        return self.payload_size == self.WRAP_MARKER


@dataclass
class BufferConfig:
    """Configuration for creating a buffer"""
    metadata_size: int = 1024
    payload_size: int = 1024 * 1024  # 1MB default
    
    def __post_init__(self):
        """Validate configuration"""
        if self.metadata_size <= 0:
            raise ValueError("metadata_size must be positive")
        if self.payload_size <= 0:
            raise ValueError("payload_size must be positive")


class Frame:
    """
    Zero-copy frame reference
    
    This class provides access to frame data without copying it from shared memory.
    The data is accessed through a memoryview, ensuring zero-copy operation.
    """
    
    def __init__(self, memory_view: memoryview, offset: int, size: int, sequence: int):
        """
        Initialize frame reference
        
        Args:
            memory_view: Memoryview of the payload buffer
            offset: Offset of frame data within the buffer
            size: Size of frame data
            sequence: Sequence number
        """
        self._memory_view = memory_view
        self._offset = offset
        self._size = size
        self._sequence = sequence
        self._data_view: Optional[memoryview] = None
    
    @property
    def data(self) -> memoryview:
        """Get zero-copy view of frame data"""
        if self._data_view is None:
            self._data_view = self._memory_view[self._offset:self._offset + self._size]
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
    
    def __bytes__(self) -> bytes:
        """Convert to bytes (creates a copy)"""
        return bytes(self.data)
    
    def __repr__(self) -> str:
        return f"Frame(sequence={self._sequence}, size={self._size})"


def align_to_boundary(size: int, alignment: int = BLOCK_ALIGNMENT) -> int:
    """Align size to specified boundary"""
    return ((size + alignment - 1) // alignment) * alignment