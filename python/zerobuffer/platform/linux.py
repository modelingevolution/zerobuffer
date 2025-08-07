"""
Linux-specific implementations for shared memory and semaphores
"""

import os
import sys
import fcntl
import mmap
import struct
import tempfile
from pathlib import Path
from typing import Optional
from multiprocessing import shared_memory

from .base import SharedMemory, Semaphore, FileLock
from ..exceptions import ZeroBufferException

# Import posix_ipc for POSIX semaphores
try:
    import posix_ipc
except ImportError:
    raise ImportError("posix_ipc is required on Linux. Install with: pip install posix-ipc")


class LinuxSharedMemory(SharedMemory):
    """Linux shared memory implementation using Python's multiprocessing.shared_memory"""
    
    def __init__(self, name: str, size: int, create: bool = False):
        self._name = name
        self._size = size
        self._shm = None
        self._buffer = None
        
        try:
            if create:
                # Create new shared memory
                self._shm = shared_memory.SharedMemory(name=name, create=True, size=size)
                # Zero the memory
                self._shm.buf[:] = b'\x00' * size
                print(f"DEBUG: Created shared memory '{name}' with size {size}, actual name: {self._shm.name}", file=sys.stderr)
                # Check if it actually exists in /dev/shm
                import subprocess
                result = subprocess.run(f"ls -la /dev/shm/ | grep {self._shm.name}", shell=True, capture_output=True, text=True)
                print(f"DEBUG: /dev/shm check: {result.stdout.strip() if result.stdout else 'NOT FOUND'}", file=sys.stderr)
            else:
                # Open existing shared memory
                self._shm = shared_memory.SharedMemory(name=name, create=False)
                self._size = self._shm.size
                print(f"DEBUG: Opened shared memory '{name}', actual name: {self._shm.name}, size: {self._size}, shm object id: {id(self._shm)}", file=sys.stderr)
                # Print first 16 bytes to verify we're looking at same memory
                first_bytes = bytes(self._shm.buf[:16])
                print(f"DEBUG: First 16 bytes: {first_bytes.hex()}", file=sys.stderr)
                # Test if we can write
                print(f"DEBUG: Testing write capability...", file=sys.stderr)
                try:
                    # Try to write a test byte
                    old_val = self._shm.buf[0]
                    self._shm.buf[0] = (old_val + 1) % 256
                    new_val = self._shm.buf[0]
                    self._shm.buf[0] = old_val  # Restore
                    print(f"DEBUG: Write test: old={old_val}, new={new_val}, restored={self._shm.buf[0]}", file=sys.stderr)
                except Exception as e:
                    print(f"DEBUG: Write test failed: {e}", file=sys.stderr)
        except FileExistsError:
            raise ZeroBufferException(f"Shared memory '{name}' already exists")
        except FileNotFoundError:
            raise ZeroBufferException(f"Shared memory '{name}' not found")
        except Exception as e:
            raise ZeroBufferException(f"Failed to create/open shared memory: {e}")
    
    def get_buffer(self) -> memoryview:
        """Get memoryview of entire shared memory buffer"""
        # Don't cache - return fresh memoryview each time to avoid sync issues
        if self._shm is not None:
            return memoryview(self._shm.buf)
        else:
            raise RuntimeError("Shared memory not initialized")
    
    def close(self) -> None:
        """Close the shared memory handle"""
        # Clear buffer reference without calling release
        # (the Reader/Writer will handle releasing their views)
        self._buffer = None
        if self._shm:
            try:
                self._shm.close()
            except BufferError:
                # Ignore BufferError - views still exist
                pass
            # Don't set to None yet - unlink() might need it
    
    def unlink(self) -> None:
        """Remove shared memory from system"""
        if self._shm:
            try:
                self._shm.unlink()
            except FileNotFoundError:
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
        if not name.startswith('/'):
            name = '/' + name
        
        try:
            if create:
                # Try to unlink first in case it exists
                try:
                    posix_ipc.unlink_semaphore(name)
                except posix_ipc.ExistentialError:
                    pass
                
                # Create new semaphore
                self._sem = posix_ipc.Semaphore(
                    name,
                    flags=posix_ipc.O_CREX,
                    mode=0o600,
                    initial_value=initial_value
                )
            else:
                # Open existing semaphore
                self._sem = posix_ipc.Semaphore(name)
        except posix_ipc.ExistentialError as e:
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
            self._fd = os.open(path, os.O_CREAT | os.O_RDWR, 0o600)
            
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