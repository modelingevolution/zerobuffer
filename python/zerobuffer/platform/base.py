"""
Abstract base classes for platform-specific implementations
"""

from abc import ABC, abstractmethod
from typing import Optional


class SharedMemory(ABC):
    """Abstract base class for platform-specific shared memory"""
    
    @abstractmethod
    def __init__(self, name: str, size: int, create: bool = False):
        """
        Initialize shared memory
        
        Args:
            name: Name of the shared memory segment
            size: Size in bytes (ignored if create=False)
            create: True to create new, False to open existing
        """
        pass
    
    @abstractmethod
    def get_buffer(self) -> memoryview:
        """Get memoryview of entire shared memory buffer"""
        pass
    
    @abstractmethod
    def close(self) -> None:
        """Close the shared memory handle"""
        pass
    
    @abstractmethod
    def unlink(self) -> None:
        """Remove shared memory from system (platform-specific)"""
        pass
    
    @property
    @abstractmethod
    def name(self) -> str:
        """Get the name of the shared memory segment"""
        pass
    
    @property
    @abstractmethod
    def size(self) -> int:
        """Get the size of the shared memory segment"""
        pass
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()


class Semaphore(ABC):
    """Abstract base class for platform-specific named semaphores"""
    
    @abstractmethod
    def __init__(self, name: str, initial_value: int = 0, create: bool = False):
        """
        Initialize semaphore
        
        Args:
            name: Name of the semaphore
            initial_value: Initial count (ignored if create=False)
            create: True to create new, False to open existing
        """
        pass
    
    @abstractmethod
    def acquire(self, timeout: Optional[float] = None) -> bool:
        """
        Acquire the semaphore
        
        Args:
            timeout: Timeout in seconds, None for infinite
            
        Returns:
            True if acquired, False if timed out
        """
        pass
    
    @abstractmethod
    def release(self) -> None:
        """Release the semaphore"""
        pass
    
    @abstractmethod
    def close(self) -> None:
        """Close the semaphore handle"""
        pass
    
    @abstractmethod
    def unlink(self) -> None:
        """Remove semaphore from system (platform-specific)"""
        pass
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()


class FileLock(ABC):
    """Abstract base class for platform-specific file locks"""
    
    @abstractmethod
    def __init__(self, path: str):
        """
        Create a file lock
        
        Args:
            path: Path to the lock file
        """
        pass
    
    @abstractmethod
    def is_locked(self) -> bool:
        """Check if the lock is currently held"""
        pass
    
    @abstractmethod
    def close(self) -> None:
        """Release and remove the lock"""
        pass
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()