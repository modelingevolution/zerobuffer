# ZeroBuffer Benchmarking Guide

This guide explains how to run benchmark tests across all three language implementations (C++, C#, Python) of ZeroBuffer.

## Overview

ZeroBuffer includes comprehensive benchmarking suites in all three languages to measure:
- **Round-trip latency**: Time for a frame to go through a relay process and back
- **Throughput**: Maximum sustained data transfer rate
- **Frame rates**: Performance at various FPS levels (30, 60, 120, 240, 500, 1000)

All benchmarks use the same test scenario: transmitting YUV420 Full HD frames (3.1MB each) through shared memory buffers.

## C++ Benchmarks

### Building
```bash
cd cpp
./build.sh Release  # Build in release mode for accurate benchmarks
```

### Running All Benchmarks
```bash
# Option 1: Using test script
./test.sh benchmark

# Option 2: Using dedicated benchmark script
./run_benchmarks.sh

# Option 3: Run from build directory
cd build
./benchmarks/benchmark_roundtrip
```

### Available C++ Benchmarks
1. **benchmark_roundtrip** - Main round-trip latency test with relay process
2. **benchmark_latency** - Various latency measurements (single frame, pipelined, wakeup)
3. **benchmark_throughput** - Throughput tests (sustained, burst, memory pressure)
4. **benchmark_scenarios** - Real-world scenarios (wrap-around, metadata, random patterns)
5. **benchmark_video** - Video-specific benchmarks

### Running Specific C++ Benchmarks
```bash
# Run with specific parameters
./benchmarks/benchmark_latency --benchmark_filter=BM_SingleFrameLatency/1024

# Run with more iterations for stability
./benchmarks/benchmark_throughput --benchmark_repetitions=10

# Output to JSON
./benchmarks/benchmark_roundtrip --benchmark_out=results.json --benchmark_out_format=json
```

## C# Benchmarks

### Building
```bash
cd csharp
dotnet build -c Release
```

### Running Benchmarks
```bash
# Run from project directory
cd ZeroBuffer.Benchmarks
dotnet run -c Release

# Or run the built executable
cd bin/Release/net9.0
./ZeroBuffer.Benchmarks
```

### C# Benchmark Features
- Automatically spawns relay process
- Tests multiple FPS levels sequentially
- Provides detailed latency percentiles (P50, P90, P99)
- Measures frame loss and validates delivery

## Python Benchmarks

### Setup
```bash
cd python
./build.sh  # Creates virtual environment and installs dependencies
```

### Running Round-Trip Benchmark
```bash
# Activate virtual environment
source venv/bin/activate

# Run the benchmark
python benchmark_roundtrip.py

# Or using the test script
./test.sh performance
```

### Python-Specific Options
The Python benchmark can be customized:
```python
# Inside benchmark_roundtrip.py, modify:
TEST_FPS_LEVELS = [30, 60, 120, 240, 500, 1000]
TEST_DURATION = 10  # seconds per FPS level
WARMUP_FRAMES = 100
```

## Cross-Language Benchmark Comparison

### Running Cross-Platform Tests
To compare performance across languages:

```bash
# From cross-platform-tests directory
cd cross-platform-tests/round-trip

# C++ to C#
./cpp-csharp/run_test.sh

# C# to Python  
./csharp-python/run_test.sh

# Python to C++
./python-cpp/run_test.sh
```

### Expected Performance

Typical round-trip latencies (may vary by system):
- **C++**: 200-500 μs average, <1ms P99
- **C#**: 250-600 μs average, <1.5ms P99  
- **Python**: 1-2ms average, <5ms P99

## Benchmark Output Interpretation

### C++ (Google Benchmark)
```
BM_SingleFrameLatency/3110416/manual_time  245 us  312 us  2847 bytes_per_second=12.1Gi/s
```
- First number: Wall-clock time per iteration
- Second number: CPU time per iteration
- Third number: Number of iterations
- Additional metrics: Throughput calculations

### C# Output
```
--- Testing at 60 FPS ---
  Frames sent: 600, received: 600
  Round-trip latency (microseconds):
    Min:    245 μs
    Avg:    312 μs
    P50:    298 μs
    P90:    367 μs
    P99:    512 μs
    Max:    892 μs
```

### Python Output
Similar to C#, with additional timing breakdowns:
```
  Writing took: 9.823s
  Reading took: 9.825s
  Total test time: 10.002s
```

## Performance Tuning Tips

### System Preparation
```bash
# Disable CPU frequency scaling (Linux)
sudo cpupower frequency-set -g performance

# Increase shared memory limits
sudo sysctl -w kernel.shmmax=2147483648

# Pin processes to specific CPUs
taskset -c 0,1 ./benchmark_program
```

### Clean Environment
```bash
# Clean up before benchmarking
./clean.sh  # In C++ directory

# Or manually
rm -f /dev/shm/zerobuffer-*
rm -f /dev/shm/sem.zerobuffer-*
rm -f /tmp/zerobuffer/*.lock
```

### Reduce System Noise
- Close unnecessary applications
- Disable system updates
- Run benchmarks multiple times and average results
- Use dedicated hardware when possible

## Automated Benchmark Reporting

### C++ with CI
```yaml
# Example GitHub Actions workflow
- name: Run C++ Benchmarks
  run: |
    cd cpp
    ./build.sh Release
    ./benchmarks/benchmark_roundtrip --benchmark_out=results.json
    
- name: Store Benchmark Results
  uses: benchmark-action/github-action-benchmark@v1
  with:
    tool: 'googlecpp'
    output-file-path: cpp/build/results.json
```

### Python Coverage + Performance
```bash
# Run performance tests with coverage
cd python
./test.sh coverage

# Or specific performance test
pytest tests/test_scenarios.py::TestScenario11Performance -v
```

## Troubleshooting

### Common Issues

1. **"Failed to open semaphore"**
   - Run cleanup: `./clean.sh`
   - Check permissions on /dev/shm

2. **High variance in results**
   - Ensure system is idle
   - Disable CPU throttling
   - Run with higher repetitions

3. **Python slower than expected**
   - Ensure using release build of Python
   - Check GIL contention
   - Verify virtual environment is activated

### Debug Mode
```bash
# C++ debug output
export ZEROBUFFER_LOG_LEVEL=debug
./benchmark_roundtrip

# Python debug
export ZEROBUFFER_LOG_LEVEL=DEBUG
python benchmark_roundtrip.py
```

## Summary

Each language provides its own benchmarking tools:
- **C++**: Google Benchmark-based suite with detailed micro-benchmarks
- **C#**: Integrated round-trip benchmark with automatic relay spawning
- **Python**: Standalone round-trip benchmark with comprehensive timing

All three measure the same core metric: round-trip latency for video frames through a relay process, making cross-language performance comparison straightforward.