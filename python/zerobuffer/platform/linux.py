"""
Linux-specific implementations for shared memory and semaphores
"""

import os
import fcntl
import mmap
import ctypes
from pathlib import Path
from typing import Optional

# Removed multiprocessing.shared_memory - using posix_ipc instead

from .base import SharedMemory, Semaphore, FileLock
from ..exceptions import ZeroBufferException

# Import posix_ipc for POSIX semaphores
try:
    import posix_ipc
except ImportError:
    raise ImportError("posix_ipc is required on Linux. Install with: pip install posix-ipc")


def memory_barrier() -> None:
    """
    Implement a memory barrier to ensure all memory operations are visible across CPU cores.
    Equivalent to Thread.MemoryBarrier() in C#.

    Note: Python on Linux doesn't have direct access to CPU memory fence instructions.
    We use OS-level synchronization primitives that include memory barriers.
    """
    # The most reliable approach is to use an actual memory fence via inline assembly
    # But since we can't do that directly in Python, we use OS synchronization

    # For mmap'd shared memory, the most effective approach is to use msync
    # This is what we'll do in the flush() method of LinuxSharedMemory

    # Here we just ensure the Python interpreter flushes any cached values
    # by forcing a context switch
    libc = ctypes.CDLL("libc.so.6")
    # sched_yield() causes the calling thread to relinquish the CPU
    # This can help with cache coherency on some systems
    sched_yield = libc.sched_yield
    sched_yield()


class LinuxSharedMemory(SharedMemory):
    """Linux shared memory implementation using Python's multiprocessing.shared_memory"""

    def __init__(self, name: str, size: int, create: bool = False):
        self._name = name
        self._size = size
        self._shm = None
        self._mapfile = None

        # Use name as-is for consistency with C# and C++
        # The name should already include '/' if needed

        try:
            if create:
                # Create new shared memory with 0666 permissions
                flags = posix_ipc.O_CREAT | posix_ipc.O_EXCL | posix_ipc.O_RDWR
                self._shm = posix_ipc.SharedMemory(name, flags=flags, mode=0o666, size=size)

                # Map the shared memory
                self._mapfile = mmap.mmap(self._shm.fd, size)

                # Zero the memory
                self._mapfile[:] = b"\x00" * size
            else:
                # Open existing shared memory
                self._shm = posix_ipc.SharedMemory(name)

                # Map the shared memory
                self._mapfile = mmap.mmap(self._shm.fd, self._shm.size)
                self._size = self._shm.size
        except posix_ipc.ExistentialError:
            if create:
                raise ZeroBufferException(f"Shared memory '{name}' already exists")
            else:
                raise ZeroBufferException(f"Shared memory '{name}' not found")
        except Exception as e:
            raise ZeroBufferException(f"Failed to create/open shared memory: {e}")

    def get_buffer(self) -> memoryview:
        """Get memoryview of entire shared memory buffer"""
        # Don't cache - return fresh memoryview each time to avoid sync issues
        if self._mapfile is not None:
            return memoryview(self._mapfile)
        else:
            raise RuntimeError("Shared memory not initialized")

    def flush(self) -> None:
        """Flush shared memory to ensure all writes are visible to other processes"""
        if self._mapfile is not None:
            # Use msync with MS_SYNC flag to ensure memory coherency
            # This is the proper way to synchronize mmap'd memory between processes
            # Python's mmap.flush() calls msync internally
            # We use both offset and size parameters for better control
            self._mapfile.flush(0, len(self._mapfile))

            # Also call memory barrier for additional safety
            memory_barrier()

    def close(self) -> None:
        """Close the shared memory handle"""
        # Close the memory map
        if self._mapfile:
            try:
                self._mapfile.close()
            except Exception:
                pass
            self._mapfile = None

        # Close the file descriptor
        if self._shm:
            try:
                self._shm.close_fd()
            except Exception:
                pass
            # Don't set to None yet - unlink() might need it

    def unlink(self) -> None:
        """Remove shared memory from system"""
        if self._shm:
            try:
                self._shm.unlink()
            except (FileNotFoundError, posix_ipc.ExistentialError):
                pass  # Already removed
            # Now we can clear the reference
            self._shm = None

    @property
    def name(self) -> str:
        return self._name

    @property
    def size(self) -> int:
        return self._size


class LinuxSemaphore(Semaphore):
    """Linux semaphore implementation using POSIX semaphores"""

    def __init__(self, name: str, initial_value: int = 0, create: bool = False):
        self._name = name
        self._sem = None

        # Ensure name starts with /
        if not name.startswith("/"):
            name = "/" + name

        try:
            if create:
                # Try to unlink first in case it exists
                try:
                    posix_ipc.unlink_semaphore(name)
                except posix_ipc.ExistentialError:
                    pass

                # Create new semaphore
                self._sem = posix_ipc.Semaphore(
                    name, flags=posix_ipc.O_CREX, mode=0o666, initial_value=initial_value  # Read/write for all
                )
            else:
                # Open existing semaphore
                self._sem = posix_ipc.Semaphore(name)
        except posix_ipc.ExistentialError:
            if create:
                raise ZeroBufferException(f"Semaphore '{name}' already exists")
            else:
                raise ZeroBufferException(f"Semaphore '{name}' not found")
        except Exception as e:
            raise ZeroBufferException(f"Failed to create/open semaphore: {e}")

    def acquire(self, timeout: Optional[float] = None) -> bool:
        """Acquire the semaphore"""
        if not self._sem:
            raise ZeroBufferException("Semaphore is closed")

        try:
            if timeout is None:
                self._sem.acquire()
                return True
            else:
                # posix_ipc expects timeout in seconds
                self._sem.acquire(timeout)
                return True
        except posix_ipc.BusyError:
            return False
        except Exception as e:
            raise ZeroBufferException(f"Failed to acquire semaphore: {e}")

    def release(self) -> None:
        """Release the semaphore"""
        if not self._sem:
            raise ZeroBufferException("Semaphore is closed")

        try:
            self._sem.release()
        except Exception as e:
            raise ZeroBufferException(f"Failed to release semaphore: {e}")

    def close(self) -> None:
        """Close the semaphore handle"""
        if self._sem:
            self._sem.close()
            self._sem = None

    def unlink(self) -> None:
        """Remove semaphore from system"""
        if self._sem:
            try:
                self._sem.unlink()
            except posix_ipc.ExistentialError:
                pass  # Already removed


class LinuxFileLock(FileLock):
    """Linux file lock implementation using fcntl.flock"""

    def __init__(self, path: str):
        self._path = path
        self._fd = None
        self._locked = False

        # Create directory if it doesn't exist
        Path(path).parent.mkdir(parents=True, exist_ok=True)

        try:
            # Open or create the lock file
            self._fd = os.open(path, os.O_CREAT | os.O_RDWR, 0o666)  # Read/write for all

            # Try to acquire exclusive lock (non-blocking)
            fcntl.flock(self._fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self._locked = True
        except BlockingIOError:
            # Lock is held by another process
            if self._fd is not None:
                os.close(self._fd)
            self._fd = None
            raise ZeroBufferException(f"Failed to acquire lock: {path}")
        except Exception as e:
            if self._fd is not None:
                os.close(self._fd)
                self._fd = None
            raise ZeroBufferException(f"Failed to create lock file: {e}")

    def is_locked(self) -> bool:
        """Check if the lock is currently held"""
        return self._locked and self._fd is not None

    def close(self) -> None:
        """Release and remove the lock"""
        if self._fd is not None:
            try:
                # Release the lock
                fcntl.flock(self._fd, fcntl.LOCK_UN)
                os.close(self._fd)

                # Remove the lock file
                try:
                    os.unlink(self._path)
                except OSError:
                    pass  # File may have been removed already
            finally:
                self._fd = None
                self._locked = False

    @staticmethod
    def try_remove_stale(path: str) -> bool:
        """Try to remove a stale lock file"""
        try:
            fd = os.open(path, os.O_RDWR)
            try:
                # Try to acquire exclusive lock (non-blocking)
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                # We got the lock, so the file is stale
                fcntl.flock(fd, fcntl.LOCK_UN)
                os.close(fd)
                os.unlink(path)
                return True
            except BlockingIOError:
                # Lock is held by another process
                os.close(fd)
                return False
        except (OSError, IOError):
            return False
