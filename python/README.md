# ZeroBuffer Python Implementation

Native Python implementation of the ZeroBuffer protocol for high-performance zero-copy inter-process communication.

## Features

- **True Zero-Copy**: Uses memoryview and buffer protocol to avoid data copies
- **Cross-Platform**: Supports Linux, Windows, and macOS
- **Protocol Compatible**: Binary compatible with C++ and C# implementations
- **Pythonic API**: Clean, idiomatic Python interface
- **Type Safe**: Full type hints for better IDE support
- **Thread Safe**: Built-in synchronization for multi-threaded applications

## Requirements

- Python 3.8 or later
- `posix-ipc` (Linux/macOS only): `pip install posix-ipc`
- `pywin32` (Windows only): `pip install pywin32`

## Installation

```bash
pip install -e .
```

## Quick Start

### Reader Example

```python
from zerobuffer import Reader, BufferConfig

# Create a buffer
config = BufferConfig(metadata_size=1024, payload_size=1024*1024)
with Reader("my-buffer", config) as reader:
    # Wait for writer to connect
    print("Waiting for writer...")
    
    # Read frames
    while True:
        frame = reader.read_frame(timeout=5.0)
        if frame:
            # Access data without copying
            data = frame.data  # This is a memoryview
            print(f"Received frame {frame.sequence}: {len(data)} bytes")
            
            # Process the frame...
            
            # Must release frame to free buffer space
            reader.release_frame(frame)
```

### Writer Example

```python
from zerobuffer import Writer

# Connect to existing buffer
with Writer("my-buffer") as writer:
    # Write some metadata
    metadata = b"{'format': 'raw', 'version': 1}"
    writer.set_metadata(metadata)
    
    # Write frames
    for i in range(100):
        data = f"Frame {i}".encode()
        writer.write_frame(data)
        print(f"Sent frame {i}")
```

### Zero-Copy Advanced Usage

```python
# True zero-copy writing with memoryview
import numpy as np

# Create numpy array
arr = np.arange(1000, dtype=np.float32)

# Get memoryview of array (no copy)
view = memoryview(arr)

# Write with zero-copy
writer.write_frame_zero_copy(view)

# Or use direct buffer access
buffer = writer.get_frame_buffer(size=4000)
# Write directly into shared memory
buffer[:] = arr.tobytes()
writer.commit_frame()
```

## API Reference

### Reader Class

```python
Reader(name: str, config: Optional[BufferConfig] = None)
```

Creates a new buffer and prepares for reading.

**Methods:**
- `get_metadata() -> Optional[memoryview]`: Get metadata as zero-copy memoryview
- `read_frame(timeout: Optional[float] = 5.0) -> Optional[Frame]`: Read next frame
- `release_frame(frame: Frame) -> None`: Release frame and free buffer space
- `is_writer_connected() -> bool`: Check if writer is connected
- `close() -> None`: Close reader and clean up resources

### Writer Class

```python
Writer(name: str)
```

Connects to an existing buffer for writing.

**Methods:**
- `set_metadata(data: Union[bytes, bytearray, memoryview]) -> None`: Write metadata (once only)
- `write_frame(data: Union[bytes, bytearray, memoryview]) -> None`: Write a frame
- `write_frame_zero_copy(data: memoryview) -> None`: Write frame with zero-copy
- `get_frame_buffer(size: int) -> memoryview`: Get buffer for direct writing
- `commit_frame() -> None`: Commit frame after direct writing
- `is_reader_connected() -> bool`: Check if reader is connected
- `close() -> None`: Close writer

### Frame Class

Represents a zero-copy reference to frame data.

**Properties:**
- `data -> memoryview`: Zero-copy view of frame data
- `size -> int`: Size of frame data
- `sequence -> int`: Sequence number

### BufferConfig Class

```python
BufferConfig(metadata_size: int = 1024, payload_size: int = 1024*1024)
```

Configuration for creating a buffer.

## Zero-Copy Guarantees

The Python implementation provides true zero-copy access through:

1. **memoryview objects**: No data copying when accessing frame data
2. **Buffer protocol**: Direct memory access for compatible objects
3. **Shared memory**: Direct mapping of shared memory into process space

### When Copies Occur

- Converting memoryview to bytes: `bytes(frame.data)`
- Using non-buffer protocol objects with `write_frame()`
- String encoding: `"text".encode()`

### Avoiding Copies

- Use `memoryview` objects whenever possible
- Use `write_frame_zero_copy()` for memoryview data
- Use numpy arrays or other buffer protocol objects
- Access frame data directly via `frame.data` memoryview

## Performance Considerations

1. **Pre-allocate buffers**: Reuse buffers instead of creating new ones
2. **Batch operations**: Process multiple frames before releasing
3. **Use appropriate buffer sizes**: See capacity planning in main README
4. **Monitor buffer utilization**: Avoid buffer full conditions

## Error Handling

All operations may raise exceptions from `zerobuffer.exceptions`:

- `WriterDeadException`: Writer process died
- `ReaderDeadException`: Reader process died  
- `BufferFullException`: Buffer is full
- `FrameTooLargeException`: Frame exceeds buffer capacity
- `SequenceError`: Frame sequence validation failed

## Thread Safety

The Reader and Writer classes are thread-safe. Multiple threads can:
- Call methods on the same Reader/Writer instance
- Read frames concurrently (with proper frame release)
- Write frames concurrently

However, only one Reader and one Writer can connect to a buffer at a time.

## Platform Notes

### Linux
- Uses POSIX shared memory (`/dev/shm`)
- Requires `posix-ipc` package
- File locks in `/tmp/zerobuffer/`

### Windows
- Uses Windows named shared memory
- Requires `pywin32` package
- File locks in temp directory

### macOS
- Similar to Linux with BSD-specific handling
- Requires `posix-ipc` package

## Testing Utilities

The Python implementation includes testing utilities for cross-platform compatibility with C# and C++ implementations:

### BufferNamingService

Ensures unique buffer names across test runs to prevent conflicts:

```python
from zerobuffer_serve.services import BufferNamingService

# Creates unique buffer names for test isolation
naming_service = BufferNamingService(logger)
actual_name = naming_service.get_buffer_name("test-buffer")
# Returns: "test-buffer_<pid>_<timestamp>" or uses Harmony environment variables
```

### TestDataPatterns

Provides consistent test data generation across all language implementations:

```python
from zerobuffer_serve.test_data_patterns import TestDataPatterns

# Generate deterministic frame data
data = TestDataPatterns.generate_frame_data(size=1024, sequence=1)

# Generate simple pattern data
simple_data = TestDataPatterns.generate_simple_frame_data(size=1024)

# Verify data matches pattern
is_valid = TestDataPatterns.verify_simple_frame_data(data)

# Generate test metadata
metadata = TestDataPatterns.generate_metadata(size=256)
```

These utilities ensure that Python, C#, and C++ implementations can exchange data correctly in cross-platform tests.