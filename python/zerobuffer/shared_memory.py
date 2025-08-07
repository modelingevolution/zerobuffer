"""
Proper SharedMemory abstraction for ZeroBuffer

This module provides a clean, tested abstraction over Python's multiprocessing.shared_memory
that ensures reliable cross-process memory sharing.
"""

import struct
from typing import Optional, Any
from multiprocessing import shared_memory as mp_shared_memory


class SharedMemory:
    """
    Clean abstraction for shared memory with proper read/write semantics.
    
    Similar to C#'s ISharedMemory interface but adapted for Python.
    """
    
    def __init__(self, name: str, size: int = 0, create: bool = False):
        """
        Create or open shared memory.
        
        Args:
            name: Name of the shared memory segment
            size: Size in bytes (required only when creating)
            create: True to create new, False to open existing
        """
        self._name = name
        self._size = size
        self._shm: Optional[mp_shared_memory.SharedMemory] = None
        
        if create:
            if size <= 0:
                raise ValueError("Size must be positive when creating shared memory")
            # Create new shared memory
            self._shm = mp_shared_memory.SharedMemory(name=name, create=True, size=size)
            # Initialize to zeros
            self.write_bytes(0, b'\x00' * size)
        else:
            # Open existing shared memory
            self._shm = mp_shared_memory.SharedMemory(name=name, create=False)
            self._size = self._shm.size
    
    @property
    def name(self) -> str:
        """Get the name of the shared memory segment."""
        return self._name
    
    @property
    def size(self) -> int:
        """Get the size of the shared memory segment."""
        return self._size
    
    def read_bytes(self, offset: int, length: int) -> bytes:
        """
        Read bytes from shared memory.
        
        Args:
            offset: Byte offset to start reading from
            length: Number of bytes to read
            
        Returns:
            Bytes read from shared memory
        """
        if not self._shm:
            raise RuntimeError("Shared memory is closed")
        if offset < 0 or offset + length > self._size:
            raise ValueError(f"Invalid read range: offset={offset}, length={length}, size={self._size}")
        
        # Important: Convert to bytes to get a copy, not a view
        return bytes(self._shm.buf[offset:offset + length])
    
    def write_bytes(self, offset: int, data: bytes) -> None:
        """
        Write bytes to shared memory.
        
        Args:
            offset: Byte offset to start writing at
            data: Bytes to write
        """
        if not self._shm:
            raise RuntimeError("Shared memory is closed")
        if offset < 0 or offset + len(data) > self._size:
            raise ValueError(f"Invalid write range: offset={offset}, length={len(data)}, size={self._size}")
        
        # Direct slice assignment to the buffer
        self._shm.buf[offset:offset + len(data)] = data
    
    def read_struct(self, offset: int, format_string: str) -> tuple[object, ...]:
        """
        Read a struct from shared memory.
        
        Args:
            offset: Byte offset to read from
            format_string: struct format string (e.g., '<Q' for little-endian uint64)
            
        Returns:
            Tuple of unpacked values
        """
        size = struct.calcsize(format_string)
        data = self.read_bytes(offset, size)
        return struct.unpack(format_string, data)
    
    def write_struct(self, offset: int, format_string: str, *values: object) -> None:
        """
        Write a struct to shared memory.
        
        Args:
            offset: Byte offset to write at
            format_string: struct format string
            *values: Values to pack and write
        """
        data = struct.pack(format_string, *values)
        self.write_bytes(offset, data)
    
    def read_uint64(self, offset: int) -> int:
        """Read a little-endian uint64 at the given offset."""
        result = self.read_struct(offset, '<Q')[0]
        # Cast is safe since we know '<Q' format returns an int
        assert isinstance(result, int)
        return result
    
    def write_uint64(self, offset: int, value: int) -> None:
        """Write a little-endian uint64 at the given offset."""
        self.write_struct(offset, '<Q', value)
    
    def get_memoryview(self, offset: int = 0, length: Optional[int] = None) -> memoryview:
        """
        Get a memoryview for direct access (use with caution).
        
        This is for advanced users who need zero-copy access.
        Changes to the memoryview will directly affect shared memory.
        
        Args:
            offset: Starting offset
            length: Length of view (None for remaining bytes)
            
        Returns:
            Memoryview of the shared memory region
        """
        if not self._shm:
            raise RuntimeError("Shared memory is closed")
        
        if length is None:
            length = self._size - offset
        
        if offset < 0 or offset + length > self._size:
            raise ValueError(f"Invalid view range: offset={offset}, length={length}, size={self._size}")
        
        return memoryview(self._shm.buf)[offset:offset + length]
    
    def flush(self) -> None:
        """
        Flush any pending writes (no-op on Linux, here for API compatibility).
        """
        # Python's shared_memory uses mmap which should be coherent
        # This is here for API compatibility with C#
        pass
    
    def close(self) -> None:
        """Close the shared memory handle (but don't unlink it)."""
        if self._shm:
            try:
                self._shm.close()
            except BufferError:
                # Ignore BufferError if there are still exported pointers
                # This can happen if memoryviews are still active
                pass
            self._shm = None
    
    def unlink(self) -> None:
        """Remove the shared memory from the system."""
        if self._shm:
            try:
                self._shm.unlink()
            except FileNotFoundError:
                pass  # Already unlinked
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()
    
    def __del__(self):
        self.close()


class SharedMemoryFactory:
    """Factory for creating SharedMemory instances (similar to C#'s pattern)."""
    
    @staticmethod
    def create(name: str, size: int) -> SharedMemory:
        """Create new shared memory."""
        return SharedMemory(name, size, create=True)
    
    @staticmethod
    def open(name: str) -> SharedMemory:
        """Open existing shared memory."""
        return SharedMemory(name, create=False)
    
    @staticmethod
    def remove(name: str) -> None:
        """
        Remove shared memory from the system.
        
        This is a utility method to clean up orphaned shared memory.
        """
        try:
            shm = mp_shared_memory.SharedMemory(name=name, create=False)
            shm.unlink()
            shm.close()
        except FileNotFoundError:
            pass  # Already removed