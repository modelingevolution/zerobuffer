# Python Duplex Channel Implementation

## Overview

The Python implementation of the ZeroBuffer Duplex Channel provides bidirectional request-response communication with full cross-platform compatibility.

## Features

### Implemented (v1.0.0)

- **Immutable Duplex Server**: Processes requests and returns new response data
- **Duplex Client**: Sends requests and receives correlated responses
- **RAII Frame Management**: Automatic resource cleanup using context managers
- **Cross-Platform Compatibility**: Full interoperability with C++ and C# implementations
- **Request-Response Correlation**: Uses Frame sequence numbers for matching
- **Timeout Handling**: Configurable timeouts for receive operations

### Architecture

```
┌──────────────┐         ┌──────────────┐
│    Client    │         │    Server    │
└──────┬───────┘         └──────┬───────┘
       │                         │
       │    Request Buffer       │
       │ ──────────────────────► │
       │  (channel_request)      │
       │                         │
       │    Response Buffer      │
       │ ◄────────────────────── │
       │  (channel_response)     │
       │                         │
```

## API Usage

### Server Implementation

```python
from zerobuffer.duplex import DuplexChannelFactory, ProcessingMode, ImmutableDuplexServer
from zerobuffer import BufferConfig

# Create server
factory = DuplexChannelFactory()
config = BufferConfig(metadata_size=4096, payload_size=1024*1024)
server = factory.create_immutable_server("my-channel", config)

# Define handler
def echo_handler(frame):
    """Echo handler - returns the same data"""
    # Frame is automatically disposed when handler returns
    return bytes(frame.data)

# Start processing
server.start(echo_handler, ProcessingMode.SINGLE_THREAD)

# Server runs in background thread
# Stop when done
server.stop()
```

### Client Implementation

```python
from zerobuffer.duplex import DuplexChannelFactory, DuplexClient

# Create client
factory = DuplexChannelFactory()
client = factory.create_client("my-channel")

# Send request
data = b"Hello, World!"
sequence = client.send_request(data)

# Receive response
response = client.receive_response(timeout_ms=5000)

# Verify and process response
if response.is_valid and response.sequence == sequence:
    with response:  # Context manager ensures proper disposal
        result = bytes(response.data)
        print(f"Received: {result}")
```

## RAII and Resource Management

The Python implementation uses context managers to implement RAII (Resource Acquisition Is Initialization) pattern:

### Frame Disposal

```python
# Manual disposal with context manager
response = client.receive_response(timeout_ms=5000)
with response:
    # Frame is valid here
    process_data(response.data)
# Frame is automatically disposed here, semaphore signaled

# Automatic disposal in server handlers
def handler(frame):
    # Frame is valid here
    result = process(frame.data)
    return result
    # Frame is automatically disposed when handler returns
```

### Semaphore Signaling

The Frame disposal mechanism ensures proper semaphore signaling:
- When a Frame is disposed (via context manager exit or handler return)
- The read semaphore is signaled to notify the writer that buffer space is available
- This prevents buffer exhaustion and deadlocks

## Cross-Platform Compatibility

The Python implementation is fully compatible with C++ and C# implementations:

### Protocol Compliance
- Uses standard ZeroBuffer protocol v1.0.0
- OIEB structure matches C++/C# exactly (128 bytes)
- Frame header format identical (8B size + 8B sequence)
- Semaphore naming convention: `sem-w-{name}`, `sem-r-{name}`

### Tested Combinations
- ✅ Python client ↔ Python server
- ✅ Python client ↔ C++ server  
- ✅ Python client ↔ C# server
- ✅ C++ client ↔ Python server
- ✅ C# client ↔ Python server

## Implementation Details

### File Structure
```
zerobuffer/duplex/
├── __init__.py           # Public API exports
├── interfaces.py         # Abstract base classes
├── factory.py           # DuplexChannelFactory
├── server.py            # ImmutableDuplexServer, MutableDuplexServer
├── client.py            # DuplexClient
└── processing_mode.py   # ProcessingMode enum
```

### Threading Model
- **Server**: Runs handler in background thread
- **Client**: Thread-safe for concurrent send/receive
- **ProcessingMode.SINGLE_THREAD**: Sequential request processing
- **ProcessingMode.THREAD_POOL**: (Future) Parallel processing

### Error Handling
```python
from zerobuffer.exceptions import (
    ReaderDeadException,    # Server process died
    WriterDeadException,     # Client process died
    TimeoutException,        # Operation timed out
    BufferFullException      # No space in buffer
)
```

## Performance Characteristics

- **Latency**: Sub-millisecond for local IPC
- **Throughput**: Limited by shared memory bandwidth
- **Memory**: Zero-copy read operations via memory views
- **CPU**: Minimal overhead, semaphore-based synchronization

## Testing

Run the duplex channel tests:

```bash
# Unit tests
python -m pytest zerobuffer/duplex/tests/

# Integration tests (requires harmony test framework)
cd /path/to/zerobuffer
./test.sh python 13.1  # Test scenario 13.1

# Cross-platform tests
./test.sh python_cpp 13.1   # Python client, C++ server
./test.sh cpp_python 13.1   # C++ client, Python server
```

## Future Enhancements (v2.0.0)

- **Mutable Duplex Server**: True zero-copy in-place modifications
- **Async Support**: `async`/`await` for server handlers
- **Thread Pool Processing**: Parallel request handling
- **Shared Payload Buffers**: Memory optimization for duplex channels
- **Performance Optimizations**: Further latency reductions

## Known Limitations

1. **Single Client**: Only one client per duplex channel (ZeroBuffer limitation)
2. **No Encryption**: Designed for trusted local IPC only
3. **Memory Views**: Python's memory management prevents true zero-copy writes
4. **GIL Impact**: Python's Global Interpreter Lock may affect throughput

## Migration from v0.x

If upgrading from earlier versions:

1. Frame now requires explicit disposal via context manager
2. DuplexChannelFactory no longer accepts logger parameter
3. Server handlers must return bytes (immutable) or None (mutable)
4. Client receive methods return Frame objects, not raw data

## Examples

See `examples/duplex/` for complete working examples:
- `echo_server.py` - Simple echo server
- `echo_client.py` - Client sending requests
- `image_processor.py` - Image processing server
- `benchmark.py` - Performance benchmarking