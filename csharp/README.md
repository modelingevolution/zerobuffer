# ZeroBuffer C# Implementation

A high-performance, zero-copy **cross-process** inter-process communication (IPC) library for .NET 9+.

**IMPORTANT**: ZeroBuffer is specifically designed for communication between separate processes, not for in-process communication. For in-process scenarios, use standard .NET collections like Channel<T> or concurrent collections.

## Overview

This is the C# implementation of the ZeroBuffer protocol, providing cross-language compatible shared memory communication for video streaming applications.

## Features

- **Zero-copy shared memory** using .NET's MemoryMappedFile
- **Cross-process synchronization** using .NET's Semaphore
- **Memory-safe access** using ReadOnlyMemory<T> and Span<T>
- **Cross-language compatible** - uses plain POD structures matching C++ implementation
- **Modern C# features** - leveraging .NET 9+ capabilities

## Requirements

- .NET 9.0 or later
- Windows, Linux, or macOS

## Usage

### Basic Example

```csharp
// Create a reader (owns the buffer)
var config = new BufferConfig(
    metadataSize: 1024,      // 1KB for metadata
    payloadSize: 10 * 1024 * 1024  // 10MB for frames
);

using var reader = new ReaderSimplified("my-buffer", config);

// Create a writer (connects to existing buffer)
using var writer = new WriterSimplified("my-buffer");

// Write metadata (optional, once only)
var metadata = Encoding.UTF8.GetBytes("Camera settings...");
writer.SetMetadata(metadata);

// Write frames
var frameData = GetVideoFrame(); // your video data
writer.WriteFrame(frameData);

// Read frames
var frame = reader.ReadFrame();
if (frame.IsValid)
{
    Console.WriteLine($"Got frame {frame.Sequence}, size: {frame.Size}");
    ProcessFrame(frame.Span); // Direct access to data
}
```

### Cross-Process Example

Process 1 (Reader):
```csharp
var config = new BufferConfig(1024, 50 * 1024 * 1024); // 50MB buffer
using var reader = new ReaderSimplified("video-stream", config);

while (true)
{
    var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
    if (frame.IsValid)
    {
        // Process frame data
        DisplayFrame(frame.Data);
    }
}
```

Process 2 (Writer):
```csharp
using var writer = new WriterSimplified("video-stream");

while (capturing)
{
    var frameData = CaptureFrame();
    writer.WriteFrame(frameData);
}
```

## Protocol Compatibility

The C# implementation uses the exact same memory layout as the C++ version:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 128)]
public struct OIEB
{
    public ulong OperationSize;
    public ulong MetadataSize;
    public ulong MetadataFreeBytes;
    public ulong MetadataWrittenBytes;
    public ulong PayloadSize;
    public ulong PayloadFreeBytes;
    public ulong PayloadWritePos;
    public ulong PayloadReadPos;
    public ulong PayloadWrittenCount;
    public ulong PayloadReadCount;
    public ulong WriterPid;
    public ulong ReaderPid;
    // 32 bytes padding
}
```

## Building

```bash
# Build
dotnet build

# Run tests
dotnet test

# Create NuGet package
dotnet pack -c Release
```

## Implementation Notes

1. **Memory Safety**: Uses unsafe pointers wrapped in safe Span<T> for zero-copy access
2. **Single Reader/Writer**: Designed for single reader and single writer per buffer
3. **Resource Cleanup**: Implements IDisposable with proper cleanup of all resources
4. **Platform Support**: Uses .NET's cross-platform abstractions for Windows/Linux/macOS

## Performance

**Note**: All performance measurements should be done between separate processes, as this library is optimized for cross-process IPC, not in-process communication.

The implementation leverages:
- True zero-copy access to frame data via unsafe pointers and Span<T>
- Direct memory access via MemoryMappedFile
- Lock-free reading where possible
- Minimal allocations using Span<T> and ref structs
- Efficient semaphore-based cross-process synchronization
- Aggressive inlining and modern C# optimizations

## Error Handling

The library throws specific exceptions for different error conditions:
- `BufferNotFoundException` - Buffer doesn't exist
- `BufferFullException` - No space for new frames
- `WriterDeadException` - Writer process has died
- `ReaderDeadException` - Reader process has died
- `FrameTooLargeException` - Frame exceeds buffer capacity

## License

Same as the main project.