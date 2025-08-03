"""
Platform abstraction layer for ZeroBuffer

Provides cross-platform implementations for shared memory and semaphores.
"""

import platform
import sys
from typing import Union

from .base import SharedMemory, Semaphore, FileLock

# Import platform-specific implementations
if sys.platform == 'linux':
    from .linux import LinuxSharedMemory, LinuxSemaphore, LinuxFileLock
    PlatformSharedMemory = LinuxSharedMemory
    PlatformSemaphore = LinuxSemaphore
    PlatformFileLock = LinuxFileLock
elif sys.platform == 'win32':
    from .windows import WindowsSharedMemory, WindowsSemaphore, WindowsFileLock
    PlatformSharedMemory = WindowsSharedMemory
    PlatformSemaphore = WindowsSemaphore
    PlatformFileLock = WindowsFileLock
elif sys.platform == 'darwin':
    from .darwin import DarwinSharedMemory, DarwinSemaphore, DarwinFileLock
    PlatformSharedMemory = DarwinSharedMemory
    PlatformSemaphore = DarwinSemaphore
    PlatformFileLock = DarwinFileLock
else:
    raise NotImplementedError(f"Platform {sys.platform} is not supported")


def create_shared_memory(name: str, size: int) -> SharedMemory:
    """Create a new shared memory segment"""
    return PlatformSharedMemory(name, size, create=True)


def open_shared_memory(name: str) -> SharedMemory:
    """Open an existing shared memory segment"""
    return PlatformSharedMemory(name, 0, create=False)


def create_semaphore(name: str, initial_value: int = 0) -> Semaphore:
    """Create a new semaphore"""
    return PlatformSemaphore(name, initial_value, create=True)


def open_semaphore(name: str) -> Semaphore:
    """Open an existing semaphore"""
    return PlatformSemaphore(name, 0, create=False)


def create_file_lock(path: str) -> FileLock:
    """Create a file lock"""
    return PlatformFileLock(path)


def get_temp_directory() -> str:
    """Get platform-specific temp directory for lock files"""
    if sys.platform == 'win32':
        import tempfile
        return tempfile.gettempdir()
    else:
        return "/tmp/zerobuffer"


def process_exists(pid: int) -> bool:
    """Check if a process with given PID exists"""
    if pid == 0:
        return False
    
    try:
        if sys.platform == 'win32':
            import psutil
            return psutil.pid_exists(pid)
        else:
            import os
            import signal
            os.kill(pid, signal.SIG_DFL)
            return True
    except (OSError, ProcessLookupError):
        return False
    except ImportError:
        # Fallback if psutil not available on Windows
        import os
        try:
            os.kill(pid, 0)
            return True
        except (OSError, ProcessLookupError):
            return False


__all__ = [
    'SharedMemory',
    'Semaphore',
    'FileLock',
    'create_shared_memory',
    'open_shared_memory',
    'create_semaphore',
    'open_semaphore',
    'create_file_lock',
    'get_temp_directory',
    'process_exists'
]