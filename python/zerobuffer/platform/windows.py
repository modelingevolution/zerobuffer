"""
Windows-specific implementations for shared memory and semaphores
"""

import os
import mmap
import time
import struct
import tempfile
from pathlib import Path
from typing import Optional, Any
from multiprocessing import shared_memory

from .base import SharedMemory, Semaphore, FileLock
from ..exceptions import ZeroBufferException

# Import Windows-specific modules
import sys
if sys.platform == 'win32':
    try:
        import win32api
        import win32con
        import win32event
        import win32file
        import pywintypes
    except ImportError:
        raise ImportError("pywin32 is required on Windows. Install with: pip install pywin32")
else:
    # Dummy implementations for type checking on non-Windows platforms
    class DummyModule:
        def __getattr__(self, name: str) -> Any:
            return 0
    
    win32api = DummyModule()
    win32con = DummyModule()
    win32event = DummyModule()
    win32file = DummyModule()
    pywintypes = DummyModule()


class WindowsSharedMemory(SharedMemory):
    """Windows shared memory implementation"""
    
    def __init__(self, name: str, size: int, create: bool = False):
        self._name = name
        self._size = size
        self._shm: Optional[shared_memory.SharedMemory] = None
        self._buffer: Optional[memoryview] = None
        
        try:
            if create:
                # Create new shared memory
                self._shm = shared_memory.SharedMemory(name=name, create=True, size=size)
                # Zero the memory
                self._shm.buf[:] = b'\x00' * size
            else:
                # Open existing shared memory
                self._shm = shared_memory.SharedMemory(name=name, create=False)
                self._size = self._shm.size
        except FileExistsError:
            raise ZeroBufferException(f"Shared memory '{name}' already exists")
        except FileNotFoundError:
            raise ZeroBufferException(f"Shared memory '{name}' not found")
        except Exception as e:
            raise ZeroBufferException(f"Failed to create/open shared memory: {e}")
    
    def get_buffer(self) -> memoryview:
        """Get memoryview of entire shared memory buffer"""
        if self._buffer is None:
            self._buffer = memoryview(self._shm.buf)
        return self._buffer
    
    def close(self) -> None:
        """Close the shared memory handle"""
        if self._shm:
            self._shm.close()
            self._shm = None
            self._buffer = None
    
    def unlink(self) -> None:
        """Remove shared memory from system"""
        if self._shm:
            try:
                self._shm.unlink()
            except FileNotFoundError:
                pass  # Already removed
    
    @property
    def name(self) -> str:
        return self._name
    
    @property
    def size(self) -> int:
        return self._size


class WindowsSemaphore(Semaphore):
    """Windows semaphore implementation using Win32 API"""
    
    def __init__(self, name: str, initial_value: int = 0, create: bool = False):
        self._name = name
        self._handle = None
        
        # Windows semaphore names need specific format
        sem_name = f"Global\\{name}" if not name.startswith("Global\\") else name
        
        try:
            if create:
                # Create new semaphore
                self._handle = win32event.CreateSemaphore(
                    None,  # Security attributes
                    initial_value,  # Initial count
                    0x7FFFFFFF,  # Maximum count
                    sem_name  # Name
                )
                
                if self._handle is None:
                    raise ZeroBufferException("Failed to create semaphore")
                
                # Check if it already existed
                if win32api.GetLastError() == win32con.ERROR_ALREADY_EXISTS:
                    win32api.CloseHandle(self._handle)
                    raise ZeroBufferException(f"Semaphore '{name}' already exists")
            else:
                # Open existing semaphore
                self._handle = win32event.OpenSemaphore(
                    win32con.SEMAPHORE_ALL_ACCESS,  # Desired access
                    False,  # Inherit handle
                    sem_name  # Name
                )
                
                if self._handle is None:
                    raise ZeroBufferException(f"Semaphore '{name}' not found")
                    
        except pywintypes.error as e:
            if e.winerror == 2:  # ERROR_FILE_NOT_FOUND
                raise ZeroBufferException(f"Semaphore '{name}' not found")
            else:
                raise ZeroBufferException(f"Failed to create/open semaphore: {e}")
    
    def acquire(self, timeout: Optional[float] = None) -> bool:
        """Acquire the semaphore"""
        if not self._handle:
            raise ZeroBufferException("Semaphore is closed")
        
        try:
            if timeout is None:
                timeout_ms = win32event.INFINITE
            else:
                timeout_ms = int(timeout * 1000)
            
            result = win32event.WaitForSingleObject(self._handle, timeout_ms)
            
            if result == win32event.WAIT_OBJECT_0:
                return True
            elif result == win32event.WAIT_TIMEOUT:
                return False
            else:
                raise ZeroBufferException(f"Failed to acquire semaphore: {result}")
                
        except Exception as e:
            raise ZeroBufferException(f"Failed to acquire semaphore: {e}")
    
    def release(self) -> None:
        """Release the semaphore"""
        if not self._handle:
            raise ZeroBufferException("Semaphore is closed")
        
        try:
            win32event.ReleaseSemaphore(self._handle, 1)
        except Exception as e:
            raise ZeroBufferException(f"Failed to release semaphore: {e}")
    
    def close(self) -> None:
        """Close the semaphore handle"""
        if self._handle:
            try:
                win32api.CloseHandle(self._handle)
            except:
                pass
            self._handle = None
    
    def unlink(self) -> None:
        """Remove semaphore from system"""
        # Windows doesn't have explicit unlink for semaphores
        # They are removed when all handles are closed
        self.close()


class WindowsFileLock(FileLock):
    """Windows file lock implementation"""
    
    def __init__(self, path: str):
        self._path = path
        self._handle = None
        self._locked = False
        
        # Create directory if it doesn't exist
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        
        try:
            # Create or open the lock file
            self._handle = win32file.CreateFile(
                path,
                win32con.GENERIC_READ | win32con.GENERIC_WRITE,
                0,  # No sharing
                None,  # Security attributes
                win32con.CREATE_ALWAYS,
                win32con.FILE_ATTRIBUTE_NORMAL,
                None  # Template file
            )
            
            if self._handle == win32file.INVALID_HANDLE_VALUE:
                raise ZeroBufferException(f"Failed to create lock file: {path}")
            
            # Try to lock the file
            try:
                win32file.LockFileEx(
                    self._handle,
                    win32con.LOCKFILE_EXCLUSIVE_LOCK | win32con.LOCKFILE_FAIL_IMMEDIATELY,
                    0,
                    0xFFFFFFFF,
                    0xFFFFFFFF,
                    pywintypes.OVERLAPPED()
                )
                self._locked = True
            except pywintypes.error:
                # Lock is held by another process
                win32api.CloseHandle(self._handle)
                self._handle = None
                raise ZeroBufferException(f"Failed to acquire lock: {path}")
                
        except Exception as e:
            if self._handle and self._handle != win32file.INVALID_HANDLE_VALUE:
                win32api.CloseHandle(self._handle)
                self._handle = None
            raise ZeroBufferException(f"Failed to create lock file: {e}")
    
    def is_locked(self) -> bool:
        """Check if the lock is currently held"""
        return self._locked and self._handle is not None
    
    def close(self) -> None:
        """Release and remove the lock"""
        if self._handle and self._handle != win32file.INVALID_HANDLE_VALUE:
            try:
                # Unlock the file
                if self._locked:
                    win32file.UnlockFileEx(
                        self._handle,
                        0,
                        0xFFFFFFFF,
                        0xFFFFFFFF,
                        pywintypes.OVERLAPPED()
                    )
                
                # Close the handle
                win32api.CloseHandle(self._handle)
                
                # Remove the lock file
                try:
                    os.unlink(self._path)
                except OSError:
                    pass  # File may have been removed already
                    
            finally:
                self._handle = None
                self._locked = False
    
    @staticmethod
    def try_remove_stale(path: str) -> bool:
        """Try to remove a stale lock file"""
        try:
            # Try to open with exclusive access
            handle = win32file.CreateFile(
                path,
                win32con.GENERIC_READ | win32con.GENERIC_WRITE,
                0,  # No sharing
                None,
                win32con.OPEN_EXISTING,
                win32con.FILE_ATTRIBUTE_NORMAL,
                None
            )
            
            if handle == win32file.INVALID_HANDLE_VALUE:
                return False
            
            # Try to lock
            try:
                win32file.LockFileEx(
                    handle,
                    win32con.LOCKFILE_EXCLUSIVE_LOCK | win32con.LOCKFILE_FAIL_IMMEDIATELY,
                    0,
                    0xFFFFFFFF,
                    0xFFFFFFFF,
                    pywintypes.OVERLAPPED()
                )
                
                # We got the lock, file is stale
                win32file.UnlockFileEx(
                    handle,
                    0,
                    0xFFFFFFFF,
                    0xFFFFFFFF,
                    pywintypes.OVERLAPPED()
                )
                win32api.CloseHandle(handle)
                os.unlink(path)
                return True
                
            except pywintypes.error:
                # Lock is held by another process
                win32api.CloseHandle(handle)
                return False
                
        except (OSError, pywintypes.error):
            return False