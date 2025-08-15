# ZeroBuffer Protocol Specification

ZeroBuffer is a language-agnostic, high-performance inter-process communication protocol designed for zero-copy data exchange between processes.

## Overview

The protocol defines a shared memory ring buffer with semaphore-based synchronization, supporting single writer and single reader patterns. It's optimized for streaming applications where low latency and high throughput are critical.

## Protocol Structure

### Components

1. **Shared Memory Buffer** - Named shared memory segment containing:
   - Operation Info Exchange Block (OIEB)
   - Metadata Block
   - Payload Block (Ring Buffer)

2. **Semaphores** - Two named semaphores for synchronization:
   - `sem-w-{name}` - Signals data is available for reading
   - `sem-r-{name}` - Signals space is available for writing

3. **File Lock** - Platform-specific lock file for resource management

### Memory Layout

```
+------------------------+ 0x0000
| OIEB (128 bytes)      |
| - 64-byte aligned     |
+------------------------+ 0x0080
| Metadata Block        |
| - Variable size       |
| - 64-byte aligned     |
+------------------------+
| Payload Block         |
| - Ring buffer         |
| - Variable size       |
| - 64-byte aligned     |
+------------------------+
```

### OIEB (Operation Info Exchange Block) Structure

The OIEB is a 128-byte structure that manages the shared memory buffer state. It provides version information and tracks buffer usage for cross-language compatibility:

| Offset | Field Name              | Size | Description                                      |
|--------|-------------------------|------|--------------------------------------------------|
| 0x00   | oieb_size               | 4B   | Total OIEB size (uint32, always 128)           |
| 0x04   | version                 | 4B   | Protocol version (4 bytes: major.minor.patch.reserved) |
| 0x08   | metadata_size           | 8B   | Total metadata block size (uint64)             |
| 0x10   | metadata_free_bytes     | 8B   | Free bytes in metadata block (uint64)          |
| 0x18   | metadata_written_bytes  | 8B   | Written bytes in metadata block (uint64)       |
| 0x20   | payload_size            | 8B   | Total payload block size (uint64)              |
| 0x28   | payload_free_bytes      | 8B   | Free bytes in payload block (uint64)           |
| 0x30   | payload_write_pos       | 8B   | Current write position in ring buffer (uint64) |
| 0x38   | payload_read_pos        | 8B   | Current read position in ring buffer (uint64)  |
| 0x40   | payload_written_count   | 8B   | Number of frames written (uint64)              |
| 0x48   | payload_read_count      | 8B   | Number of frames read (uint64)                 |
| 0x50   | writer_pid              | 8B   | Writer process ID (uint64, 0 if none)          |
| 0x58   | reader_pid              | 8B   | Reader process ID (uint64, 0 if none)          |
| 0x60   | reserved_1              | 8B   | Reserved for future use (uint64)               |
| 0x68   | reserved_2              | 8B   | Reserved for future use (uint64)               |
| 0x70   | reserved_3              | 8B   | Reserved for future use (uint64)               |
| 0x78   | reserved_4              | 8B   | Reserved for future use (uint64)               |

### Protocol Version Structure

The version field is a 4-byte structure that identifies the protocol version:

```
struct ProtocolVersion {
    uint8_t major;     // Major version (breaking changes)
    uint8_t minor;     // Minor version (new features, backward compatible)
    uint8_t patch;     // Patch version (bug fixes)
    uint8_t reserved;  // Reserved for future use (must be 0)
};
```

**Version Field Format:**
- Byte 0: Major version (0-255)
- Byte 1: Minor version (0-255)
- Byte 2: Patch version (0-255)
- Byte 3: Reserved (must be 0)

**Version Compatibility Rules:**
- Different major versions are incompatible
- Same major, newer minor: Reader can read if it understands all features
- Same major and minor: Fully compatible regardless of patch

**Protocol Requirements:**
- Total structure size: 128 bytes (v1.x.x.x), 1024 bytes (v2.x.x.x)
- Byte order: Little-endian
- Alignment: 64-byte boundary
- Current version: 1.0.0.0
- Process IDs used for liveness detection
- Reserved fields must be set to 0

### Version History

#### Version 1.0.0.0 (Current)
- OIEB size: 128 bytes
- Payload location: Always in same shared memory segment
- Single buffer architecture

#### Version 2.0.0.0 (Future - Shared Payload Support)
- OIEB size: 1024 bytes
- New fields:
  - `0x60`: `payload_location` (8B) - Enum: 0=same memory, 1=external shared memory
  - `0x68`: `payload_shm_name` (264B) - External shared memory name (8B size prefix + max 256B string)
  - `0x170-0x3FF`: Additional reserved space for future extensions
- Enables:
  - Shared payload buffer between request/response channels (DuplexChannel optimization)
  - True zero-copy for in-place modifications
  - 50% memory reduction for duplex operations
  - External memory mapping for large payloads

## Implementations

- **C++** - Native implementation in `cpp/` directory
- **C#** - .NET implementation in `csharp/` directory
- **Python** - Python implementation in `python/` directory

All implementations follow the same protocol specification and are fully interoperable.

## Getting Started

See the README.md in each language directory for implementation-specific build and usage instructions.

## Protocol Details

For complete protocol specification, see [README.md](README.md)