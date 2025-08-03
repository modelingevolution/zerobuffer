# ZeroBuffer Benchmarks

This project contains cross-process round-trip latency benchmarks for the ZeroBuffer IPC library.

## Overview

The benchmark measures the round-trip latency of transmitting YUV420 Full HD frames between two processes using shared memory. This simulates real-world video streaming scenarios where frames need to be passed between different components with minimal latency.

## Architecture

The benchmark uses a two-buffer design to measure true cross-process latency without clock synchronization issues:

```
┌─────────────────┐                    ┌─────────────────┐
│   Benchmark     │                    │     Relay       │
│    Process      │                    │    Process      │
├─────────────────┤                    ├─────────────────┤
│                 │                    │                 │
│  Writer ─────┬──┼──── Buffer A ─────┼──> Reader       │
│              │  │                    │       │         │
│              │  │                    │       ↓         │
│  Reader <────┴──┼──── Buffer B ─────┼─── Writer       │
│                 │                    │                 │
└─────────────────┘                    └─────────────────┘
```

1. **Benchmark Process**: Writes timestamped frames to Buffer A and reads the echoed frames from Buffer B
2. **Relay Process**: Reads frames from Buffer A and immediately writes them to Buffer B
3. **Timestamp Embedding**: Each frame contains a timestamp in its header, allowing accurate latency measurement without clock synchronization

## Measured Metrics

- **Round-Trip Latency**: Time from writing a frame to Buffer A until reading it back from Buffer B
- **Percentiles**: P50, P90, P99 latencies to understand distribution
- **Frame Loss**: Tracks if any frames are lost during transmission

## Test Parameters

- **Frame Size**: YUV420 Full HD (1920x1080) = 3,110,400 bytes + 16 byte header
- **Buffer Size**: 256 MB (can hold ~82 Full HD frames)
- **Target FPS**: Tests run at 30, 60, 120, 240, 500, and 1000 FPS
- **Test Duration**: 10 seconds per FPS level (unkown frame count);
- **Warmup**: 100 frames before measurement begins

## Frame Structure

Each frame consists of:
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TimestampedFrame
{
    public long Timestamp;  // 8 bytes - Stopwatch ticks
    public int FrameId;     // 4 bytes
    public int Padding;     // 4 bytes - align to 16 bytes
    // Followed by YUV420 data (3,110,400 bytes)
}
```

## Buffer Initialization Protocol

The benchmark follows a specific initialization sequence to ensure both buffers are ready:

1. Start relay process
2. Relay creates Buffer A (input buffer) as Reader
3. Benchmark creates Buffer B (output buffer) as Reader
4. Benchmark connects to Buffer A as Writer
5. Relay waits for Buffer B to exist, then connects as Writer
6. Both processes are now ready for the benchmark

## Running the Benchmarks

```bash
cd ZeroBuffer.Benchmarks
dotnet run -c Release
```

## Interpreting Results

Example output:
```
--- Testing at 60 FPS ---
  Warming up... done
  Measuring 1000 frames... done
  Frames sent: 1000, received: 1000
  Round-trip latency (microseconds):
    Min:    245 μs
    Avg:    312 μs
    P50:    298 μs
    P90:    367 μs
    P99:    512 μs
    Max:    892 μs
```

- **Min**: Best-case latency, typically seen when both processes are actively scheduled
- **Avg**: Average latency across all frames
- **P50**: Median latency - 50% of frames have lower latency
- **P90**: 90% of frames have lower latency than this value
- **P99**: 99% of frames have lower latency - good indicator of worst-case behavior
- **Max**: Worst-case latency observed

## Performance Considerations

1. **OS Scheduling**: Process scheduling can significantly impact latency, especially at high FPS
2. **CPU Affinity**: Consider pinning processes to specific cores for consistent results
3. **Memory Pressure**: Large buffers may cause page faults; ensure sufficient RAM
4. **Timer Resolution**: PeriodicTimer has 1ms minimum resolution, limiting accuracy above 1000 FPS

## Limitations

- **Maximum FPS**: Limited by PeriodicTimer resolution (1ms minimum)
- **Single Frame Size**: Currently tests only Full HD YUV420 frames
- **Unidirectional**: Measures only one direction (though round-trip)
- **No Network**: Only tests local shared memory, not network scenarios