"""
Shared memory abstraction with platform-specific implementation.

This module provides a clean abstraction for shared memory with proper
read/write semantics, delegating to platform-specific implementations.
"""

import struct
import sys

# Import platform-specific implementation
if sys.platform == "linux":
    from .platform.linux import LinuxSharedMemory as PlatformSharedMemory
elif sys.platform == "darwin":
    from .platform.darwin import DarwinSharedMemory as PlatformSharedMemory
elif sys.platform == "win32":
    from .platform.windows import WindowsSharedMemory as PlatformSharedMemory
else:
    raise ImportError(f"Unsupported platform: {sys.platform}")


class SharedMemory:
    """
    Clean abstraction for shared memory with proper read/write semantics.

    Delegates to platform-specific implementation.
    """

    def __init__(self, name: str, size: int = 0, create: bool = False):
        """
        Create or open shared memory.

        Args:
            name: Name of the shared memory segment
            size: Size in bytes (required only when creating)
            create: True to create new, False to open existing
        """
        # For POSIX systems, ensure name starts with /
        if sys.platform in ("linux", "darwin") and not name.startswith("/"):
            name = "/" + name

        self._impl = PlatformSharedMemory(name, size, create)
        self._name = name
        self._size = size if size > 0 else self._impl._size

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
        if offset < 0 or offset + length > self._size:
            raise ValueError(f"Invalid read range: [{offset}:{offset+length}] for size {self._size}")

        buf = self._impl.get_buffer()
        return bytes(buf[offset : offset + length])

    def write_bytes(self, offset: int, data: bytes) -> None:
        """
        Write bytes to shared memory.

        Args:
            offset: Byte offset to start writing at
            data: Bytes to write
        """
        if offset < 0 or offset + len(data) > self._size:
            raise ValueError(f"Invalid write range: [{offset}:{offset+len(data)}] for size {self._size}")

        buf = self._impl.get_buffer()
        buf[offset : offset + len(data)] = data

    def read_uint32(self, offset: int) -> int:
        """Read a uint32 from shared memory."""
        data = self.read_bytes(offset, 4)
        return int(struct.unpack("<I", data)[0])

    def write_uint32(self, offset: int, value: int) -> None:
        """Write a uint32 to shared memory."""
        data = struct.pack("<I", value)
        self.write_bytes(offset, data)

    def read_uint64(self, offset: int) -> int:
        """Read a uint64 from shared memory."""
        data = self.read_bytes(offset, 8)
        return int(struct.unpack("<Q", data)[0])

    def write_uint64(self, offset: int, value: int) -> None:
        """Write a uint64 to shared memory."""
        data = struct.pack("<Q", value)
        self.write_bytes(offset, data)

    def get_view(self, offset: int, length: int) -> memoryview:
        """
        Get a memoryview of a specific region of shared memory.

        Args:
            offset: Byte offset to start the view
            length: Length of the view in bytes

        Returns:
            Memoryview of the specified region
        """
        if offset < 0 or offset + length > self._size:
            raise ValueError(f"Invalid view range: [{offset}:{offset+length}] for size {self._size}")

        buf = self._impl.get_buffer()
        return buf[offset : offset + length]

    def get_memoryview(self, offset: int, length: int) -> memoryview:
        """
        Alias for get_view for compatibility.

        Args:
            offset: Byte offset to start the view
            length: Length of the view in bytes

        Returns:
            Memoryview of the specified region
        """
        return self.get_view(offset, length)

    def flush(self) -> None:
        """Flush shared memory to ensure all writes are visible to other processes."""
        if hasattr(self._impl, "flush"):
            self._impl.flush()

    def close(self) -> None:
        """Close the shared memory handle."""
        if hasattr(self._impl, "close"):
            self._impl.close()

    def unlink(self) -> None:
        """Remove shared memory from the system."""
        if hasattr(self._impl, "unlink"):
            self._impl.unlink()


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
            # For POSIX systems, ensure name starts with /
            if sys.platform in ("linux", "darwin") and not name.startswith("/"):
                name = "/" + name

            shm = PlatformSharedMemory(name, 0, create=False)
            shm.unlink()
            shm.close()
        except Exception:
            pass  # Already removed or doesn't exist
