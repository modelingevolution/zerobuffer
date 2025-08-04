# ZeroBuffer C++ Implementation

Native C++ implementation of the ZeroBuffer protocol for high-performance inter-process communication.

## Features

- Zero-copy data access via shared memory
- Lock-free ring buffer implementation
- POSIX semaphore synchronization (Linux)
- Windows synchronization primitives (Windows)
- Template-based metadata access
- Comprehensive error handling with crash detection

## Requirements

- C++17 or later
- CMake 3.20+
- Google Test (fetched automatically)
- Linux: POSIX shared memory and semaphores
- Windows: Windows API

## Building

```bash
# Quick build
./build.sh

# Manual build
mkdir build
cd build
cmake ..
make -j$(nproc)
```

## Running Tests

```bash
# Run all tests
./test.sh

# Run specific test
./build/tests/test_zerobuffer
```

## Usage Example

```cpp
#include <zerobuffer/zerobuffer.h>

// Reader process
zerobuffer::Reader reader("my-buffer", zerobuffer::BufferConfig(1024, 1024*1024));

// Writer process  
zerobuffer::Writer writer("my-buffer");

// Write data
std::vector<uint8_t> data = {1, 2, 3, 4, 5};
writer.write_frame(data);

// Read data
zerobuffer::Frame frame = reader.read_frame(std::chrono::seconds(5)); // 5 second timeout
// Process frame.data(), frame.size()
reader.release_frame(frame);
```

## API Reference

### Reader Class
- `Reader(name, config)` - Create buffer and prepare for reading
- `get_metadata()` - Get metadata as byte vector
- `get_metadata_as<T>()` - Get metadata as typed pointer (zero-copy)
- `read_frame(timeout)` - Read next frame with timeout (returns invalid frame on timeout)
- `release_frame(frame)` - Release frame and free buffer space
- `is_writer_connected()` - Check if writer is currently connected
- `is_writer_connected(timeout_ms)` - Wait for writer connection with timeout

### Writer Class
- `Writer(name)` - Connect to existing buffer
- `set_metadata(data, size)` - Set metadata (once only)
- `write_frame(data, size)` - Write frame (blocks if buffer full)

### Configuration
- `BufferConfig(metadata_size, payload_size)` - Buffer configuration
- Sizes are automatically aligned to 64-byte boundaries

## Examples

See the `examples/` directory for more usage examples.

## Implementation Notes

- Single writer, single reader model
- Automatic crash detection with 5-second timeout
- Process PIDs stored in OIEB for monitoring
- Stale resource cleanup on initialization