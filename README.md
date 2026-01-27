# ZeroBuffer

[![NuGet](https://img.shields.io/nuget/v/ZeroBuffer.svg)](https://www.nuget.org/packages/ZeroBuffer/)
[![PyPI](https://img.shields.io/pypi/v/zerobuffer-ipc.svg)](https://pypi.org/project/zerobuffer-ipc/)
[![vcpkg](https://img.shields.io/badge/vcpkg-zerobuffer-blue)](https://github.com/modelingevolution/zerobuffer-vcpkg-registry)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A high-performance, zero-copy inter-process communication library with implementations in C++, C#, and Python. This ring-buffer implementation is designed for low-latency data transfer between processes, particularly for video streaming applications.

**Supported platforms**: Linux, Windows, macOS (Python/C#), with platform-specific optimizations.

The protocol is based on structure called zero-buffer(name):
- shared-memory (SHMB) defined by ${name}
- named-semaphore called sem-w-${name} (write semaphore): signaled by writer when new data is available for reading
- named-semaphore called sem-r-${name} (read semaphore): signaled by reader when data has been consumed and buffer space is available for writing

The SHMB:
- Fixed size Operation info exchange block (64-byte aligned)
- Fixed size Metadata block (64-byte aligned)
- Fixed size Payload block (64-byte aligned)

- Each item in the Payload's buffer is defined by the structure:
8B: Payload size
8B: Payload sequence number (unsigned long long)
dynamic: Payload data

## Operation info exchange block (OIEB)

Operation exchange block is used to inform reader and writer about how much data has been written or read.
8B: Operation size

8B: Metadata size
8B: Metadata free bytes
8B: Metadata written bytes

8B: Payload size
8B: Payload free bytes
8B: Payload written bytes
8B: Payload written count
8B: Payload read count

8B: Writer PID (process ID of writer, 0 if no writer attached)
8B: Reader PID (process ID of reader, 0 if no reader attached)

## Metadata block
Metadata is a structure that allows the writer to specify detailed information about the schema it will write. The reader might not know exactly what data to expect. The metadata block has the following structure:
- 8B: Metadata size (excluding this size field)
- Dynamic: Metadata content (e.g., protobuf serialized schema)

## Use case:

A Python docker container (reader) uses the zerobuffer to exchange data with streamer (writer) that would run in a different container on the same host machine. The python docker container would create zero-buffer and writes zero-buffer connection-string in json-rpc on std-out, that is started with '>' char:

> { "jsonrpc": "2.0", "method": "start-stream", "params": [ "e39640a7-50e4-4261-ac92-d9e09e480074", 1024, 2048] }

the rocket-welder2 reads this from log and thus can run gstreamer with proper parameters passed to the pipeline.

gst-launch-1.0 testvidoersrc ! zero-sink name="e39640a7-50e4-4261-ac92-d9e09e480074"

the zero-sink plugin, would then write the metadata and than frames to the zero-buffer based on the passed name: "e39640a7-50e4-4261-ac92-d9e09e480074". 

## Reader preparation:

- creates a file lock: `/tmp/zerobuffer/{name}.lock` (Linux) or appropriate temp directory (Windows)
- checks if resources should be cleaned up - tries to delete any stale *.lock files, if successful, deletes correlated resources (shared memory, semaphores)
- creates shared memory segment with identifier `${name}` (using POSIX shm_open on Linux, CreateFileMapping on Windows)
- zeros memory and initializes OIEB structure
- creates 2 named semaphores: `sem-w-${name}` and `sem-r-${name}` (using POSIX sem_open on Linux, CreateSemaphore on Windows)

## The write flow:

- The write client queries the size of metadata size and data size.

- The client verifies there is enough free bytes in metadata block
- The client writes metadata to shared memory. The metadata is prefixed with its size (protocol-level), can be then serialized via protobuf (application-level, not protocol level).

Given the client wants to write a frame (certain amount of bytes):
- The client verifies how much continuous free space is available in the buffer by examining the OIEB
- If there is not enough continuous space, it waits on the sem-r-${name} semaphore (waiting for reader to free space)
- When semaphore is signaled, the check is performed again - until we have enough continuous free memory
- The client writes the frame with its size prefix and sequence number
- After writing is complete, the client updates OIEB counters (written bytes, write count)
- The client signals sem-w-${name} to notify reader that new data is available 

## The read flow:

- The reader waits on the sem-w-${name} semaphore (waiting for writer to signal new data)
- When signaled, reader examines OIEB to determine data location and size
- Reader reads the frame header (size + sequence number) and validates sequence continuity
- Reader constructs a frame object that points to the shared buffer data (zero-copy)
- When reader finishes processing the frame (which may be at a later time):
  - Updates OIEB read count
  - Updates OIEB free bytes by adding the consumed frame size
  - Signals sem-r-${name} to notify writer that buffer space is available

## Ring Buffer Wrap-Around Handling

When writing a message of size N bytes:
1. If (free_bytes >= N && continuous_free_bytes >= N):
   - Write message at current write position
2. If (free_bytes >= N && continuous_free_bytes < N):
   - Write a wrap marker (FrameHeader with payload_size=0) at current position
   - Update write position to beginning of buffer
   - Write the actual message at the beginning
   - Writer MUST NOT split messages across buffer boundary
3. If (free_bytes < N):
   - Wait for reader to free more space

Where:
- free_bytes = total free space in buffer (may be fragmented)
- continuous_free_bytes = free space from write_pos to min(read_pos, buffer_end)

### Wrap Marker Details

A wrap marker is a special FrameHeader with:
- `payload_size = 0` (indicates this is a wrap marker, not a real frame)
- `sequence_number = 0` (wrap markers don't have sequence numbers)

When the reader encounters a wrap marker:
- It updates the read position to the beginning of the buffer
- It reclaims the wasted space at the end of the buffer
- It continues reading the actual frame from the beginning
- It does NOT increment the expected sequence number

### Semaphore Signaling with Wrap Markers

**Important**: Wrap markers are implementation details, not logical frames.

- **Writer**: Signals the write semaphore ONCE after writing a frame, even if it includes a wrap marker
- **Reader**: Signals the read semaphore ONCE after reading a frame, even if it processed a wrap marker

This ensures symmetric semaphore behavior: one signal per logical frame on both sides.

## Memory Ordering and Synchronization

To ensure correct visibility of data across processes:

1. **Memory Barriers**: 
   - Writer uses memory_order_release when updating OIEB counters after writing data
   - Reader uses memory_order_acquire when reading OIEB counters before reading data
   - This ensures data written before counter update is visible after counter read

2. **OIEB Update Order**:
   - Writer: Update data → memory barrier → update written_bytes/count → signal semaphore
   - Reader: Wait semaphore → read written_bytes/count → memory barrier → read data

3. **Sequence Number Validation**:
   - Reader verifies sequence numbers are consecutive
   - Detection of corrupted/out-of-order data indicates memory corruption

## Capacity Planning

### Buffer Size Calculation

**Basic Formula:**
```
payload_size = max_frame_size * 3 + overhead
overhead = frames_in_flight * sizeof(FrameHeader)
```

Where:
- `max_frame_size`: Largest frame you'll write
- `frames_in_flight`: Expected frames between write and read
- `sizeof(FrameHeader)`: 16 bytes (8B size + 8B sequence)

### Examples:

**1080p @ 30 FPS Video (6MB frames):**
```
max_frame_size = 6MB
frames_in_flight = 3 (100ms buffer @ 30fps)
payload_size = 6MB * 3 + 3 * 16 = 18MB
Recommended: 20MB (with padding)
```

**4K @ 60 FPS Video (25MB frames):**
```
max_frame_size = 25MB
frames_in_flight = 6 (100ms buffer @ 60fps)
payload_size = 25MB * 3 + 6 * 16 = 75MB
Recommended: 80MB (with padding)
```

**Telemetry Data (1KB @ 1000Hz):**
```
max_frame_size = 1KB
frames_in_flight = 100 (100ms buffer @ 1000Hz)
payload_size = 1KB * 3 + 100 * 16 = 4.6KB
Recommended: 8KB (with padding)
```

### Rules of Thumb:
- Use 3x your maximum frame size as baseline
- Add 16 bytes per expected concurrent frame
- Round up to power of 2 for alignment efficiency
- Monitor buffer utilization; resize if consistently >80%

## Health Monitoring

The OIEB provides real-time buffer health information:

```
utilization = (payload_size - payload_free_bytes) / payload_size * 100%
```

- **Healthy**: < 80% utilization
- **Degraded**: 80-95% utilization (log warnings)
- **Critical**: > 95% utilization (imminent frame loss)

Applications should monitor `payload_free_bytes` and log warnings when entering degraded state.

## State Diagrams

### Reader States
```
[Created] -> [Waiting] <-> [Reading] -> [Processing] -> [Waiting]
                 |                           |
                 v                           v
            [Error/Dead]                [Shutdown]
```

### Writer States  
```
[Connecting] -> [Connected] -> [Writing] <-> [Blocked] -> [Writing]
      |             |             |             |
      v             v             v             v
   [Error]    [No Reader]   [Reader Dead]  [Shutdown]
```

### Error Conditions
- **Reader Dead**: Writer detects via PID check after 5s timeout
- **Writer Dead**: Reader detects via PID check after 5s timeout
- **No Reader**: Writer cannot find active reader on connect
- **Buffer Full**: Writer blocks on sem-r until space available

## Security Model

**Note**: ZeroBuffer is designed for trusted internal use only. It provides:
- No encryption of data
- No authentication between processes
- Security via filesystem permissions only
- Same-user access by default

For secure IPC, consider encrypted alternatives. ZeroBuffer optimizes for lowest latency in trusted environments.

## Documentation

📚 **[See DOCUMENTATION_INDEX.md](DOCUMENTATION_INDEX.md) for complete documentation structure**

### Quick Links
- [Protocol Specification](PROTOCOL.md) - Technical protocol details
- [API Reference](API.md) - Complete API for all languages  
- [Test Scenarios](TEST_SCENARIOS.md) - Comprehensive test coverage
- [Changelog](CHANGELOG.md) - Version history

## Installation

### C++ with vcpkg

Configure the custom registry in your `vcpkg-configuration.json`:
```json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "YOUR_BASELINE_HERE",
      "packages": ["zerobuffer"]
    }
  ]
}
```

Then install:
```bash
vcpkg install zerobuffer
```

Or use CMake with vcpkg toolchain:
```cmake
find_package(zerobuffer CONFIG REQUIRED)
target_link_libraries(your_target PRIVATE zerobuffer::zerobuffer)
```

### C# with NuGet

[![NuGet](https://img.shields.io/nuget/v/ZeroBuffer.svg)](https://www.nuget.org/packages/ZeroBuffer/)

```bash
# Package Manager Console
Install-Package ZeroBuffer

# .NET CLI
dotnet add package ZeroBuffer

# PackageReference in .csproj
<PackageReference Include="ZeroBuffer" Version="1.0.*" />
```

### Python with pip

[![PyPI](https://img.shields.io/pypi/v/zerobuffer-ipc.svg)](https://pypi.org/project/zerobuffer-ipc/)

```bash
# Install from PyPI
pip install zerobuffer-ipc

# Or install with specific version
pip install zerobuffer-ipc==1.0.0
```

## Quick Start

### C++ Example
```cpp
#include <zerobuffer/writer.h>
#include <zerobuffer/reader.h>

// Writer
zerobuffer::Writer writer("my-buffer", 1024*1024, 64*1024);
writer.connect();
writer.write(data, size);

// Reader
zerobuffer::Reader reader("my-buffer", 1024*1024, 64*1024);
auto frame = reader.read();
```

### C# Example
```csharp
using ZeroBuffer;

// Writer
using var writer = new Writer("my-buffer", 1024*1024, 64*1024);
writer.Connect();
writer.Write(data);

// Reader
using var reader = new Reader("my-buffer", 1024*1024, 64*1024);
var frame = reader.Read();
```

### Python Example
```python
import zerobuffer

# Writer
writer = zerobuffer.Writer("my-buffer", 1024*1024, 64*1024)
writer.connect()
writer.write(data)

# Reader
reader = zerobuffer.Reader("my-buffer", 1024*1024, 64*1024)
frame = reader.read()
```

## Documentation

- [API Documentation](API.md) - Detailed API reference for C++, C#, and Python
- [Cross-Platform Tests](CROSS_PLATFORM_TESTS.md) - Testing strategy and standardized interfaces
- [Benchmarking Guide](BENCHMARKING_GUIDE.md) - How to run performance benchmarks
- [Test Coverage & Inconsistencies](TEST_COVERAGE_AND_INCONSISTENCIES.md) - Unit test analysis and known issues
- [Improvements TODO](IMPROVEMENTS_TODO.md) - Planned enhancements and known issues
- [C++ vcpkg Usage](cpp/VCPKG.md) - Using ZeroBuffer with vcpkg

