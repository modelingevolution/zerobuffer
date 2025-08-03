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

## Implementations

- **C++** - Native implementation in `cpp/` directory
- **C#** - .NET implementation in `csharp/` directory (coming soon)
- **Python** - Python implementation in `python/` directory (coming soon)

All implementations follow the same protocol specification and are fully interoperable.

## Getting Started

See the README.md in each language directory for implementation-specific build and usage instructions.

## Protocol Details

For complete protocol specification, see [README.md](README.md)