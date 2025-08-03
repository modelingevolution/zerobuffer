# ZeroBuffer Python Implementation Design

## Overview

This document outlines the design for a Python implementation of the ZeroBuffer protocol, ensuring true zero-copy operation and full compatibility with existing C++ and C# implementations.

## Key Design Principles

1. **True Zero-Copy**: Use `memoryview` and buffer protocol to avoid copying data
2. **Protocol Compatibility**: Exact binary compatibility with C++ and C# implementations
3. **Pythonic API**: Follow Python best practices while maintaining performance
4. **Cross-Platform**: Support Linux, Windows, and macOS
5. **Type Safety**: Use type hints and dataclasses for clarity
6. **Resource Safety**: Proper cleanup with context managers

## Architecture

### Core Components

```
zerobuffer/
├── __init__.py          # Package exports
├── types.py             # Core data structures (OIEB, FrameHeader, etc.)
├── platform/            # Platform abstraction layer
│   ├── __init__.py
│   ├── base.py          # Abstract base classes
│   ├── linux.py         # Linux implementation
│   ├── windows.py       # Windows implementation
│   └── darwin.py        # macOS implementation
├── reader.py            # Reader implementation
├── writer.py            # Writer implementation
├── exceptions.py        # Custom exceptions
└── utils.py             # Helper functions
```

### Zero-Copy Strategy

Python's approach to zero-copy differs from C++ and C#:

1. **Shared Memory**: Use `multiprocessing.shared_memory.SharedMemory` (Python 3.8+)
2. **Memory Views**: Use `memoryview` for zero-copy slicing
3. **Buffer Protocol**: Leverage Python's buffer protocol for direct memory access
4. **Struct Module**: Use `struct` for binary serialization without intermediate copies

### Key Classes

#### 1. OIEB Structure (types.py)

```python
from dataclasses import dataclass
import struct

BLOCK_ALIGNMENT = 64

@dataclass
class OIEB:
    """Operation Info Exchange Block - must match C++ layout exactly"""
    operation_size: int
    metadata_size: int
    metadata_free_bytes: int
    metadata_written_bytes: int
    payload_size: int
    payload_free_bytes: int
    payload_write_pos: int
    payload_read_pos: int
    payload_written_count: int
    payload_read_count: int
    writer_pid: int
    reader_pid: int
    _reserved: tuple[int, int, int, int] = (0, 0, 0, 0)
    
    FORMAT = '<16Q'  # 16 unsigned 64-bit integers, little-endian
    SIZE = struct.calcsize(FORMAT)
    
    def pack(self) -> bytes:
        """Pack OIEB into bytes"""
        return struct.pack(self.FORMAT, 
            self.operation_size, self.metadata_size, 
            self.metadata_free_bytes, self.metadata_written_bytes,
            self.payload_size, self.payload_free_bytes,
            self.payload_write_pos, self.payload_read_pos,
            self.payload_written_count, self.payload_read_count,
            self.writer_pid, self.reader_pid,
            *self._reserved)
    
    @classmethod
    def unpack(cls, data: bytes) -> 'OIEB':
        """Unpack OIEB from bytes"""
        values = struct.unpack(cls.FORMAT, data)
        return cls(*values[:-4], _reserved=values[-4:])
```

#### 2. Frame Class (types.py)

```python
@dataclass
class Frame:
    """Zero-copy frame reference"""
    _memory_view: memoryview
    _offset: int
    _size: int
    sequence: int
    
    @property
    def data(self) -> memoryview:
        """Get zero-copy view of frame data"""
        return self._memory_view[self._offset:self._offset + self._size]
    
    @property
    def size(self) -> int:
        return self._size
    
    def __len__(self) -> int:
        return self._size
```

#### 3. Platform Abstraction (platform/base.py)

```python
from abc import ABC, abstractmethod
from typing import Optional
import threading

class SharedMemory(ABC):
    """Abstract base for platform-specific shared memory"""
    
    @abstractmethod
    def __init__(self, name: str, size: int, create: bool = False):
        pass
    
    @abstractmethod
    def get_buffer(self) -> memoryview:
        """Get memoryview of entire shared memory"""
        pass
    
    @abstractmethod
    def close(self):
        pass
    
    @abstractmethod
    def unlink(self):
        """Remove shared memory (platform-specific)"""
        pass

class Semaphore(ABC):
    """Abstract base for platform-specific semaphores"""
    
    @abstractmethod
    def __init__(self, name: str, initial_value: int = 0, create: bool = False):
        pass
    
    @abstractmethod
    def acquire(self, timeout: Optional[float] = None) -> bool:
        pass
    
    @abstractmethod
    def release(self):
        pass
```

#### 4. Reader Implementation (reader.py)

```python
class Reader:
    """Zero-copy reader implementation"""
    
    def __init__(self, name: str, metadata_size: int = 1024, 
                 payload_size: int = 1024 * 1024):
        self.name = name
        self._lock = threading.RLock()
        self._closed = False
        
        # Calculate aligned sizes
        oieb_size = align_to_boundary(OIEB.SIZE, BLOCK_ALIGNMENT)
        metadata_size = align_to_boundary(metadata_size, BLOCK_ALIGNMENT)
        payload_size = align_to_boundary(payload_size, BLOCK_ALIGNMENT)
        
        total_size = oieb_size + metadata_size + payload_size
        
        # Create shared memory and get buffer
        self._shm = platform.create_shared_memory(name, total_size)
        self._buffer = self._shm.get_buffer()
        
        # Create memory views for each section
        self._oieb_view = self._buffer[:oieb_size]
        self._metadata_view = self._buffer[oieb_size:oieb_size + metadata_size]
        self._payload_view = self._buffer[oieb_size + metadata_size:]
        
        # Initialize OIEB
        oieb = OIEB(
            operation_size=oieb_size,
            metadata_size=metadata_size,
            metadata_free_bytes=metadata_size,
            metadata_written_bytes=0,
            payload_size=payload_size,
            payload_free_bytes=payload_size,
            payload_write_pos=0,
            payload_read_pos=0,
            payload_written_count=0,
            payload_read_count=0,
            writer_pid=0,
            reader_pid=os.getpid()
        )
        self._oieb_view[:] = oieb.pack()
        
        # Create semaphores
        self._sem_write = platform.create_semaphore(f"sem-w-{name}", 0)
        self._sem_read = platform.create_semaphore(f"sem-r-{name}", 0)
    
    def get_metadata(self) -> Optional[memoryview]:
        """Get metadata as zero-copy memoryview"""
        with self._lock:
            oieb = self._read_oieb()
            if oieb.metadata_written_bytes == 0:
                return None
            
            # Skip size prefix, return view of actual metadata
            return self._metadata_view[8:8 + oieb.metadata_written_bytes - 8]
    
    def read_frame(self, timeout: Optional[float] = 5.0) -> Optional[Frame]:
        """Read next frame (zero-copy)"""
        with self._lock:
            while True:
                # Wait for data
                if not self._sem_write.acquire(timeout):
                    # Check if writer is dead
                    oieb = self._read_oieb()
                    if oieb.writer_pid and not process_exists(oieb.writer_pid):
                        raise WriterDeadException()
                    return None
                
                oieb = self._read_oieb()
                
                # Check for data
                if oieb.payload_written_count > oieb.payload_read_count:
                    # Read frame header
                    header_offset = oieb.payload_read_pos
                    header = FrameHeader.unpack(
                        self._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                    )
                    
                    # Handle wrap marker
                    if header.is_wrap_marker():
                        oieb.payload_read_pos = 0
                        header_offset = 0
                        header = FrameHeader.unpack(
                            self._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                        )
                    
                    # Create zero-copy frame
                    data_offset = header_offset + FrameHeader.SIZE
                    frame = Frame(
                        _memory_view=self._payload_view,
                        _offset=data_offset,
                        _size=header.payload_size,
                        sequence=header.sequence_number
                    )
                    
                    # Update read position
                    next_pos = (data_offset + header.payload_size) % oieb.payload_size
                    oieb.payload_read_pos = next_pos
                    oieb.payload_read_count += 1
                    oieb.payload_free_bytes += FrameHeader.SIZE + header.payload_size
                    
                    self._write_oieb(oieb)
                    
                    # Signal space available
                    self._sem_read.release()
                    
                    return frame
    
    def __enter__(self):
        return self
    
    def __exit__(self, *args):
        self.close()
```

#### 5. Writer Implementation (writer.py)

```python
class Writer:
    """Zero-copy writer implementation"""
    
    def __init__(self, name: str):
        self.name = name
        self._lock = threading.RLock()
        self._closed = False
        self._sequence_number = 1
        
        # Open existing shared memory
        self._shm = platform.open_shared_memory(name)
        self._buffer = self._shm.get_buffer()
        
        # Read OIEB to get layout
        oieb = OIEB.unpack(self._buffer[:OIEB.SIZE])
        
        # Create memory views
        oieb_size = oieb.operation_size
        metadata_size = oieb.metadata_size
        self._oieb_view = self._buffer[:oieb_size]
        self._metadata_view = self._buffer[oieb_size:oieb_size + metadata_size]
        self._payload_view = self._buffer[oieb_size + metadata_size:]
        
        # Check writer
        if oieb.writer_pid and process_exists(oieb.writer_pid):
            raise WriterAlreadyConnectedException()
        
        # Set our PID
        oieb.writer_pid = os.getpid()
        self._write_oieb(oieb)
        
        # Open semaphores
        self._sem_write = platform.open_semaphore(f"sem-w-{name}")
        self._sem_read = platform.open_semaphore(f"sem-r-{name}")
    
    def write_frame_zero_copy(self, data: memoryview) -> None:
        """Write frame with true zero-copy (data must be memoryview)"""
        with self._lock:
            frame_size = len(data)
            total_size = FrameHeader.SIZE + frame_size
            
            # Wait for space
            while True:
                oieb = self._read_oieb()
                
                if oieb.payload_free_bytes >= total_size:
                    break
                
                if not self._sem_read.acquire(timeout=5.0):
                    if not oieb.reader_pid or not process_exists(oieb.reader_pid):
                        raise ReaderDeadException()
                    raise BufferFullException()
            
            # Write header
            header = FrameHeader(
                payload_size=frame_size,
                sequence_number=self._sequence_number
            )
            header_offset = oieb.payload_write_pos
            self._payload_view[header_offset:header_offset + FrameHeader.SIZE] = header.pack()
            
            # Copy data directly into shared memory
            data_offset = header_offset + FrameHeader.SIZE
            self._payload_view[data_offset:data_offset + frame_size] = data
            
            # Update OIEB
            self._sequence_number += 1
            oieb.payload_write_pos = (data_offset + frame_size) % oieb.payload_size
            oieb.payload_written_count += 1
            oieb.payload_free_bytes -= total_size
            
            self._write_oieb(oieb)
            
            # Signal data available
            self._sem_write.release()
```

### Best Practices Implementation

1. **Memory Safety**:
   - Use `memoryview` for all buffer operations
   - Never create intermediate copies
   - Validate all offsets before access

2. **Thread Safety**:
   - Use `threading.RLock` for all operations
   - Atomic updates to OIEB structure
   - Proper memory barriers (via locks)

3. **Resource Management**:
   - Context managers for automatic cleanup
   - Explicit unlink() for shared memory removal
   - Proper semaphore cleanup

4. **Error Handling**:
   - Custom exceptions matching C++/C# behavior
   - Timeout handling with process liveness checks
   - Graceful degradation on errors

5. **Performance**:
   - Pre-allocated memory views
   - Struct module for efficient packing/unpacking
   - Minimal Python object creation in hot paths

## Testing Strategy

1. **Unit Tests**: Test each component in isolation
2. **Integration Tests**: Test reader/writer interaction
3. **Cross-Language Tests**: Verify compatibility with C++ and C# implementations
4. **Performance Tests**: Benchmark zero-copy operations
5. **Stress Tests**: Test under high load and edge cases

## Platform-Specific Considerations

### Linux
- Use POSIX shared memory (`/dev/shm`)
- POSIX semaphores with `posix_ipc` package
- File locks with `fcntl.flock`

### Windows
- Use Windows Named Shared Memory via `mmap`
- Windows semaphores via `win32api`
- File locks with `msvcrt.locking`

### macOS
- Similar to Linux with some BSD-specific handling
- May need special handling for semaphore names

## Dependencies

- Python 3.8+ (for SharedMemory support)
- `posix_ipc` (Linux/macOS)
- `pywin32` (Windows)
- No other external dependencies

## Security Considerations

- Same as C++/C# implementations
- No encryption or authentication
- Filesystem permissions for access control
- Document as "trusted internal use only"