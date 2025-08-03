# ZeroBuffer Performance Analysis

## Benchmark Suite Overview

The zerobuffer library includes comprehensive benchmarks using Google Benchmark to measure:

### 1. **Latency Benchmarks** (`benchmark_latency.cpp`)
- **Single Frame Latency**: Round-trip time for one frame
- **Pipelined Latency**: Latency with multiple frames in flight
- **Wakeup Latency**: Time to wake blocked reader

### 2. **Throughput Benchmarks** (`benchmark_throughput.cpp`)
- **Maximum Throughput**: Frames/second for various sizes
- **Burst Throughput**: How fast can we fill the buffer
- **Memory Pressure**: Performance with small buffers

### 3. **Scenario Benchmarks** (`benchmark_scenarios.cpp`)
- **Wrap-Around Overhead**: Cost of circular buffer wrap
- **Metadata Operations**: Metadata read/write performance
- **Random Access**: Real-world variable frame sizes
- **Setup/Teardown**: Connection establishment cost
- **High Sequence Numbers**: Performance at scale

## Running Benchmarks

```bash
# Run all benchmarks
./run_benchmarks.sh

# Run individual benchmark
./build/benchmarks/benchmark_latency

# Run with detailed output
./build/benchmarks/benchmark_latency --benchmark_repetitions=10

# Run specific benchmark
./build/benchmarks/benchmark_latency --benchmark_filter=BM_SingleFrameLatency

# Output formats
./build/benchmarks/benchmark_latency --benchmark_format=json > results.json
./build/benchmarks/benchmark_latency --benchmark_format=csv > results.csv
```

## Key Performance Metrics

### Latency Characteristics
- **Best Case**: ~1-5 microseconds (both processes ready)
- **Typical**: ~10-50 microseconds (including context switch)
- **Wakeup**: ~50-200 microseconds (blocked to active)

### Throughput Characteristics
- **Small Frames (64B-1KB)**: Limited by syscall overhead
- **Medium Frames (4KB-64KB)**: Optimal performance
- **Large Frames (1MB+)**: Limited by memory bandwidth

### Factors Affecting Performance

1. **Frame Size**
   - Smaller frames = higher overhead percentage
   - Larger frames = better throughput, worse latency

2. **Buffer Size**
   - Larger buffer = better burst handling
   - Smaller buffer = more blocking, lower memory usage

3. **CPU Affinity**
   - Same core = best latency via L1/L2 cache
   - Different cores = higher latency, better throughput

4. **System Load**
   - Context switches add 1-10 microseconds
   - Memory pressure affects large frames more

## Optimization Recommendations

### For Low Latency
```cpp
// Small buffer, small frames, same CPU core
BufferConfig config(0, 64 * 1024);  // 64KB buffer
// Use CPU affinity to pin reader/writer to same core
```

### For High Throughput
```cpp
// Large buffer, medium frames, different CPU cores
BufferConfig config(0, 100 * 1024 * 1024);  // 100MB buffer
// Use 4KB-64KB frames for optimal performance
```

### For Memory Efficiency
```cpp
// Buffer size = 2-3x typical burst size
BufferConfig config(0, max_burst_size * 3);
// Tune based on actual usage patterns
```

## Benchmark Results Interpretation

### Example Output
```
BM_SingleFrameLatency/1024      2.35 us    [64B frame latency]
BM_Throughput/1024/1048576      1.23 ms    [1KB frames, 1MB buffer]
BM_WrapAroundOverhead/4096      0.15 us    [4KB wrap overhead]
```

### What to Look For
1. **Latency Spikes**: Indicate blocking or system issues
2. **Throughput Plateaus**: Show saturation points
3. **High Variance**: Suggests system interference

## Performance Monitoring

### Runtime Statistics
```cpp
// Reader statistics
uint64_t frames = reader.frames_read();
uint64_t bytes = reader.bytes_read();

// Writer statistics  
uint64_t frames = writer.frames_written();
uint64_t bytes = writer.bytes_written();
```

### System Monitoring
```bash
# Monitor shared memory usage
ls -la /dev/shm/

# Monitor semaphore usage
ipcs -s

# Monitor CPU usage
htop -p $(pgrep your_app)
```

## Common Performance Issues

1. **High Latency**
   - Check CPU scheduling/affinity
   - Verify no memory swapping
   - Look for lock contention

2. **Low Throughput**
   - Increase buffer size
   - Use larger frames
   - Check for reader bottlenecks

3. **Inconsistent Performance**
   - Disable CPU frequency scaling
   - Use real-time scheduling
   - Isolate CPU cores

## Platform-Specific Notes

### Linux Optimization
```bash
# Increase shared memory limits
echo 1073741824 > /proc/sys/kernel/shmmax

# Use huge pages for large buffers
echo 1024 > /proc/sys/vm/nr_hugepages

# Set CPU performance governor
cpupower frequency-set -g performance
```

### Real-Time Applications
```cpp
// Use real-time scheduling
struct sched_param param;
param.sched_priority = 90;
sched_setscheduler(0, SCHED_FIFO, &param);

// Lock memory to prevent paging
mlockall(MCL_CURRENT | MCL_FUTURE);
```