# ZeroBuffer C++ Benchmarks

This directory contains cross-process round-trip latency benchmarks for the ZeroBuffer IPC library.

## Overview

The benchmark measures the round-trip latency of transmitting YUV420 Full HD frames between two processes using shared memory. This implementation follows the same architecture as the C# benchmarks.

## Architecture

The benchmark uses a two-buffer design to measure true cross-process latency:

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

## Building

From the C++ project root:

```bash
cd /path/to/zerobuffer/cpp
./build.sh Release
```

## Running the Benchmarks

From the build directory:

```bash
cd build/benchmarks
./benchmark_roundtrip
```

The benchmark will automatically:
1. Fork and start the relay process
2. Run tests at 30, 60, 120, 240, 500, and 1000 FPS
3. Measure round-trip latency for 10 seconds at each FPS level
4. Display statistics including min, avg, P50, P90, P99, and max latencies

## Test Parameters

- **Frame Size**: YUV420 Full HD (1920x1080) = 3,110,400 bytes + 16 byte header
- **Buffer Size**: 256 MB (can hold ~82 Full HD frames)
- **Test Duration**: 10 seconds per FPS level
- **Warmup**: 100 frames before measurement begins

## Frame Structure

Each frame consists of:
```cpp
struct TimestampedFrame {
    int64_t timestamp;  // 8 bytes - nanosecond timestamp
    int32_t frame_id;   // 4 bytes
    int32_t padding;    // 4 bytes - align to 16 bytes
    // Followed by YUV420 data (3,110,400 bytes)
};
```

## Example Output

```
ZeroBuffer Cross-Process Round-Trip Latency Benchmark
=====================================================
Frame size: 3110416 bytes (YUV420 1920x1080 + 16-byte header)
Buffer size: 256 MB

--- Testing at 60 FPS ---
  Warming up... done
  Measuring 600 frames... done
  Frames sent: 600, received: 600
  Round-trip latency (microseconds):
    Min:    245 μs
    Avg:    312 μs
    P50:    298 μs
    P90:    367 μs
    P99:    512 μs
    Max:    892 μs
```

## Performance Considerations

- Process scheduling can impact latency
- Higher FPS tests may show increased latency due to scheduling pressure
- Consider CPU affinity settings for consistent results
- Ensure sufficient RAM to avoid page faults

## Differences from C# Implementation

- Uses `fork()` instead of `Process.Start()` for child process
- Uses `std::chrono::high_resolution_clock` for timing
- Direct memory operations instead of `Span<T>`
- Signal-based process termination