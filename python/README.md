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
            # Option 1: Manual frame management
            data = frame.data  # This is a memoryview (zero-copy)
            print(f"Received frame {frame.sequence}: {len(data)} bytes")
            # Process the frame...
            reader.release_frame(frame)  # Must release to free buffer space
            
            # Option 2: Using context manager (RAII pattern - automatic disposal)
            with frame:
                data = frame.data  # Automatically released when exiting 'with' block
                print(f"Received frame {frame.sequence}: {len(data)} bytes")
                # Process the frame...
                # Frame is automatically released here
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

# Write with zero-copy (memoryview is handled efficiently)
writer.write_frame(view)

# Or use direct buffer access for maximum performance
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
- `write_frame(data: Union[bytes, bytearray, memoryview]) -> None`: Write a frame (zero-copy for memoryview)
- `get_frame_buffer(size: int) -> memoryview`: Get buffer for direct writing
- `commit_frame() -> None`: Commit frame after direct writing
- `is_reader_connected() -> bool`: Check if reader is connected
- `close() -> None`: Close writer

### Frame Class

Represents a zero-copy reference to frame data with RAII support.

**Properties:**
- `data -> memoryview`: Zero-copy view of frame data
- `size -> int`: Size of frame data
- `sequence -> int`: Sequence number
- `is_valid -> bool`: Check if frame has valid data

**RAII Support:**
The Frame class implements the context manager protocol for automatic resource management:

```python
# Manual management
frame = reader.read_frame()
if frame:
    process(frame.data)
    reader.release_frame(frame)  # Must release manually

# Automatic management with context manager (RAII)
frame = reader.read_frame()
if frame:
    with frame:  # Enters context, frame is valid
        process(frame.data)
    # Frame is automatically disposed/released here
```

Using the context manager ensures frames are always properly released, even if an exception occurs during processing.

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
- Pass memoryview directly to `write_frame()` for zero-copy operation
- Use numpy arrays or other buffer protocol objects
- Access frame data directly via `frame.data` memoryview
- Use `get_frame_buffer()` and `commit_frame()` for direct memory access

## Performance Considerations

1. **Pre-allocate buffers**: Reuse buffers instead of creating new ones
2. **Batch operations**: Process multiple frames before releasing
3. **Use appropriate buffer sizes**: See capacity planning in main README
4. **Monitor buffer utilization**: Avoid buffer full conditions

## Logging Configuration

The ZeroBuffer library uses Python's standard logging module. By default, it uses a NullHandler (no output) following Python library best practices.

### Enable Logging via Environment Variable

Set the `ZEROBUFFER_LOG_LEVEL` environment variable:

```bash
export ZEROBUFFER_LOG_LEVEL=DEBUG
python your_app.py
```

Or in Python:
```python
import os
os.environ['ZEROBUFFER_LOG_LEVEL'] = 'DEBUG'
import zerobuffer  # Import after setting env var
```

### Configure Logging in Your Application

```python
import logging
import zerobuffer

# Option 1: Basic configuration for debugging
logging.basicConfig(level=logging.DEBUG)

# Option 2: Configure only zerobuffer logging
logging.getLogger('zerobuffer').setLevel(logging.DEBUG)

# Add a handler to see the output
handler = logging.StreamHandler()
handler.setFormatter(logging.Formatter(
    '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
))
logging.getLogger('zerobuffer').addHandler(handler)

# Option 3: Configure specific modules
logging.getLogger('zerobuffer.reader').setLevel(logging.DEBUG)  # Only reader logs
logging.getLogger('zerobuffer.writer').setLevel(logging.DEBUG)  # Only writer logs
logging.getLogger('zerobuffer.duplex.server').setLevel(logging.DEBUG)  # Only server logs
```

### Log Levels

- **DEBUG**: Detailed information for diagnosing issues
  - Buffer creation/initialization
  - Frame read/write operations with sizes and sequences
  - Wrap-around detection and handling
  - Semaphore operations
  - OIEB state changes
- **INFO**: General operational information
  - Buffer connection/disconnection
  - Metadata operations
  - Major state changes
- **WARNING**: Important warnings that don't prevent operation
  - Missing metadata
  - Retry operations
- **ERROR**: Error conditions
  - Connection failures
  - Processing errors
  - Resource cleanup failures

## Error Handling

All operations may raise exceptions from `zerobuffer.exceptions`:

- `WriterDeadException`: Writer process died (accepts optional message)
- `ReaderDeadException`: Reader process died (accepts optional message)
- `BufferFullException`: Buffer is full
- `FrameTooLargeException`: Frame exceeds buffer capacity
- `SequenceError`: Frame sequence validation failed
- `MetadataAlreadyWrittenException`: Metadata can only be written once
- `ZeroBufferException`: Base exception for all ZeroBuffer errors

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

## Duplex Channel Support

The Python implementation includes full support for duplex channels (bidirectional request-response communication):

```python
from zerobuffer.duplex import DuplexChannelFactory, ImmutableDuplexServer, ProcessingMode
from zerobuffer import BufferConfig, Frame, Writer

# Server side
factory = DuplexChannelFactory()
config = BufferConfig(metadata_size=4096, payload_size=10*1024*1024)
server = factory.create_immutable_server("my-service", config)

def handle_request(frame: Frame, response_writer: Writer) -> None:
    """Process request and send response"""
    with frame:  # RAII - frame is automatically disposed
        # Access request data (zero-copy)
        request_data = bytes(frame.data)
        
        # Process the request
        response_data = process_data(request_data)
        
        # Send response
        response_writer.write_frame(response_data)

# Start server with single-thread processing (default)
server.start(handle_request)
# Or specify processing mode explicitly
# server.start(handle_request, mode=ProcessingMode.SINGLE_THREAD)

# Client side
client = factory.create_client("my-service")

# Send request
request_data = b"Hello, server!"
client.send_request(request_data)

# Receive response with timeout
response = client.receive_response(timeout=5.0)
if response:
    with response:  # RAII - automatically disposed
        print(f"Response: {bytes(response.data)}")
        print(f"Sequence: {response.sequence}")
```

### Server Error Handling

```python
from zerobuffer.error_event_args import ErrorEventArgs

def handle_error(args: ErrorEventArgs) -> None:
    """Handle server errors"""
    print(f"Server error: {args.exception}")

# Add error handler
server.add_error_handler(handle_error)

# Start server with metadata initialization
def on_init(metadata: memoryview) -> None:
    """Process client metadata on connection"""
    client_info = bytes(metadata).decode()
    print(f"Client connected with metadata: {client_info}")

server.start(handle_request, on_init=on_init)
```

See [DUPLEX_CHANNEL.md](DUPLEX_CHANNEL.md) for detailed documentation.