"""
OIEBView - Direct memory view for Operation Info Exchange Block

Provides zero-copy access to OIEB fields in shared memory,
matching C#'s struct overlay approach.
"""

import struct
from typing import Optional


class ProtocolVersion:
    """Protocol version with direct memory access"""

    def __init__(self, shm: memoryview, offset: int):
        self._shm = shm
        self._offset = offset

    @property
    def major(self) -> int:
        return self._shm[self._offset]

    @major.setter
    def major(self, value: int) -> None:
        self._shm[self._offset] = value

    @property
    def minor(self) -> int:
        return self._shm[self._offset + 1]

    @minor.setter
    def minor(self, value: int) -> None:
        self._shm[self._offset + 1] = value

    @property
    def patch(self) -> int:
        return self._shm[self._offset + 2]

    @patch.setter
    def patch(self, value: int) -> None:
        self._shm[self._offset + 2] = value

    @property
    def reserved(self) -> int:
        return self._shm[self._offset + 3]

    @reserved.setter
    def reserved(self, value: int) -> None:
        self._shm[self._offset + 3] = value

    def __repr__(self) -> str:
        return f"ProtocolVersion({self.major}.{self.minor}.{self.patch})"


class OIEBView:
    """
    A direct view over shared memory for OIEB access.
    No serialization or copying - just direct memory operations.

    Memory layout (128 bytes total):
    - 0-3:   oieb_size (uint32)
    - 4-7:   version (4 x uint8)
    - 8-15:  metadata_size (uint64)
    - 16-23: metadata_free_bytes (uint64)
    - 24-31: metadata_written_bytes (uint64)
    - 32-39: payload_size (uint64)
    - 40-47: payload_free_bytes (uint64)
    - 48-55: payload_write_pos (uint64)
    - 56-63: payload_read_pos (uint64)
    - 64-71: payload_written_count (uint64)
    - 72-79: payload_read_count (uint64)
    - 80-87: writer_pid (uint64)
    - 88-95: reader_pid (uint64)
    - 96-127: reserved (4 x uint64)
    """

    __slots__ = ["_shm", "_version"]  # Prevent dict, save memory
    _shm: Optional[memoryview]

    # Field definitions: (offset, size, format)
    _UINT32_FMT = "<I"
    _UINT64_FMT = "<Q"

    # Offsets for each field
    _OIEB_SIZE_OFFSET = 0
    _VERSION_OFFSET = 4
    _METADATA_SIZE_OFFSET = 8
    _METADATA_FREE_BYTES_OFFSET = 16
    _METADATA_WRITTEN_BYTES_OFFSET = 24
    _PAYLOAD_SIZE_OFFSET = 32
    _PAYLOAD_FREE_BYTES_OFFSET = 40
    _PAYLOAD_WRITE_POS_OFFSET = 48
    _PAYLOAD_READ_POS_OFFSET = 56
    _PAYLOAD_WRITTEN_COUNT_OFFSET = 64
    _PAYLOAD_READ_COUNT_OFFSET = 72
    _WRITER_PID_OFFSET = 80
    _READER_PID_OFFSET = 88

    SIZE = 128  # Total size of OIEB

    def __init__(self, shared_memory: memoryview):
        """
        Initialize view with reference to shared memory.

        Args:
            shared_memory: memoryview of at least 128 bytes
        """
        if len(shared_memory) < self.SIZE:
            raise ValueError(f"Shared memory too small: {len(shared_memory)} < {self.SIZE}")
        self._shm: Optional[memoryview] = shared_memory
        self._version = ProtocolVersion(self._shm, self._VERSION_OFFSET)

    # Helper methods for field access
    def _get_uint32(self, offset: int) -> int:
        """Read uint32 from shared memory"""
        if self._shm is None:
            raise ValueError("OIEBView has been disposed")
        data = bytes(self._shm[offset : offset + 4])
        result: int = struct.unpack(self._UINT32_FMT, data)[0]
        return result

    def _set_uint32(self, offset: int, value: int) -> None:
        """Write uint32 to shared memory"""
        if self._shm is None:
            raise ValueError("OIEBView has been disposed")
        self._shm[offset : offset + 4] = struct.pack(self._UINT32_FMT, value)

    def _get_uint64(self, offset: int) -> int:
        """Read uint64 from shared memory"""
        if self._shm is None:
            raise ValueError("OIEBView has been disposed")
        data = bytes(self._shm[offset : offset + 8])
        result: int = struct.unpack(self._UINT64_FMT, data)[0]
        return result

    def _set_uint64(self, offset: int, value: int) -> None:
        """Write uint64 to shared memory"""
        if self._shm is None:
            raise ValueError("OIEBView has been disposed")
        self._shm[offset : offset + 8] = struct.pack(self._UINT64_FMT, value)

    # Properties for each field
    @property
    def oieb_size(self) -> int:
        return self._get_uint32(self._OIEB_SIZE_OFFSET)

    @oieb_size.setter
    def oieb_size(self, value: int) -> None:
        self._set_uint32(self._OIEB_SIZE_OFFSET, value)

    @property
    def version(self) -> ProtocolVersion:
        return self._version

    @property
    def metadata_size(self) -> int:
        return self._get_uint64(self._METADATA_SIZE_OFFSET)

    @metadata_size.setter
    def metadata_size(self, value: int) -> None:
        self._set_uint64(self._METADATA_SIZE_OFFSET, value)

    @property
    def metadata_free_bytes(self) -> int:
        return self._get_uint64(self._METADATA_FREE_BYTES_OFFSET)

    @metadata_free_bytes.setter
    def metadata_free_bytes(self, value: int) -> None:
        self._set_uint64(self._METADATA_FREE_BYTES_OFFSET, value)

    @property
    def metadata_written_bytes(self) -> int:
        return self._get_uint64(self._METADATA_WRITTEN_BYTES_OFFSET)

    @metadata_written_bytes.setter
    def metadata_written_bytes(self, value: int) -> None:
        self._set_uint64(self._METADATA_WRITTEN_BYTES_OFFSET, value)

    @property
    def payload_size(self) -> int:
        return self._get_uint64(self._PAYLOAD_SIZE_OFFSET)

    @payload_size.setter
    def payload_size(self, value: int) -> None:
        self._set_uint64(self._PAYLOAD_SIZE_OFFSET, value)

    @property
    def payload_free_bytes(self) -> int:
        return self._get_uint64(self._PAYLOAD_FREE_BYTES_OFFSET)

    @payload_free_bytes.setter
    def payload_free_bytes(self, value: int) -> None:
        self._set_uint64(self._PAYLOAD_FREE_BYTES_OFFSET, value)

    @property
    def payload_write_pos(self) -> int:
        return self._get_uint64(self._PAYLOAD_WRITE_POS_OFFSET)

    @payload_write_pos.setter
    def payload_write_pos(self, value: int) -> None:
        self._set_uint64(self._PAYLOAD_WRITE_POS_OFFSET, value)

    @property
    def payload_read_pos(self) -> int:
        return self._get_uint64(self._PAYLOAD_READ_POS_OFFSET)

    @payload_read_pos.setter
    def payload_read_pos(self, value: int) -> None:
        self._set_uint64(self._PAYLOAD_READ_POS_OFFSET, value)

    @property
    def payload_written_count(self) -> int:
        return self._get_uint64(self._PAYLOAD_WRITTEN_COUNT_OFFSET)

    @payload_written_count.setter
    def payload_written_count(self, value: int) -> None:
        self._set_uint64(self._PAYLOAD_WRITTEN_COUNT_OFFSET, value)

    @property
    def payload_read_count(self) -> int:
        return self._get_uint64(self._PAYLOAD_READ_COUNT_OFFSET)

    @payload_read_count.setter
    def payload_read_count(self, value: int) -> None:
        self._set_uint64(self._PAYLOAD_READ_COUNT_OFFSET, value)

    @property
    def writer_pid(self) -> int:
        return self._get_uint64(self._WRITER_PID_OFFSET)

    @writer_pid.setter
    def writer_pid(self, value: int) -> None:
        self._set_uint64(self._WRITER_PID_OFFSET, value)

    @property
    def reader_pid(self) -> int:
        return self._get_uint64(self._READER_PID_OFFSET)

    @reader_pid.setter
    def reader_pid(self, value: int) -> None:
        self._set_uint64(self._READER_PID_OFFSET, value)

    # Computed properties
    @property
    def used_bytes(self) -> int:
        """Calculate used bytes in buffer"""
        write_pos = self.payload_write_pos
        read_pos = self.payload_read_pos
        size = self.payload_size

        if write_pos >= read_pos:
            return write_pos - read_pos
        return size - read_pos + write_pos

    def has_space_for(self, frame_size: int) -> bool:
        """Check if buffer has space for frame"""
        return self.payload_free_bytes >= frame_size

    # Atomic update methods
    def update_after_write(self, frame_size: int) -> None:
        """Update fields after writing a frame"""
        self.payload_write_pos = (self.payload_write_pos + frame_size) % self.payload_size
        self.payload_free_bytes -= frame_size
        self.payload_written_count += 1

    def update_after_read(self, frame_size: int) -> None:
        """Update fields after reading a frame"""
        self.payload_read_pos = (self.payload_read_pos + frame_size) % self.payload_size
        self.payload_free_bytes += frame_size
        self.payload_read_count += 1

    def initialize(self, metadata_size: int, payload_size: int, reader_pid: int) -> None:
        """Initialize OIEB for a new buffer"""
        self.oieb_size = 128
        self.version.major = 1
        self.version.minor = 0
        self.version.patch = 0
        self.version.reserved = 0
        self.metadata_size = metadata_size
        self.metadata_free_bytes = metadata_size
        self.metadata_written_bytes = 0
        self.payload_size = payload_size
        self.payload_free_bytes = payload_size
        self.payload_write_pos = 0
        self.payload_read_pos = 0
        self.payload_written_count = 0
        self.payload_read_count = 0
        self.writer_pid = 0
        self.reader_pid = reader_pid
        # Reserved fields are left as-is (should be zero)

    def dispose(self) -> None:
        """Release the memoryview reference to allow proper cleanup"""
        if hasattr(self, "_shm") and self._shm is not None:
            # Properly release the memoryview
            self._shm.release()
            self._shm = None

    def __repr__(self) -> str:
        return (
            f"OIEBView(size={self.oieb_size}, version={self.version}, "
            f"writer_pid={self.writer_pid}, reader_pid={self.reader_pid}, "
            f"written={self.payload_written_count}, read={self.payload_read_count})"
        )
