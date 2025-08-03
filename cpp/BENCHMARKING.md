# ZeroBuffer Benchmarking Guide

## Overview

ZeroBuffer includes a comprehensive benchmarking suite built on Google Benchmark to measure:
- **Latency**: Round-trip time for frames
- **Throughput**: Maximum frames/bytes per second
- **Scalability**: Performance under various loads
- **Edge Cases**: Wrap-around, metadata, setup costs

## Quick Start

```bash
# Build release version with benchmarks
./build_release.sh

# Run all benchmarks
./run_benchmarks.sh

# Test release build (includes basic benchmarks)
./test_release.sh
```

## Benchmark Categories

### 1. Latency Measurements (`benchmark_latency`)

**BM_SingleFrameLatency**
- Measures round-trip time for a single frame
- Tests various frame sizes (64B to 1MB)
- Shows impact of frame size on latency

**BM_PipelinedLatency**
- Measures latency with multiple frames in flight
- Tests pipeline depths (1, 10, 100 frames)
- Shows how buffering affects latency

**BM_WakeupLatency**
- Measures time to wake a blocked reader
- Tests semaphore signaling overhead
- Critical for understanding blocking behavior

### 2. Throughput Measurements (`benchmark_throughput`)

**BM_Throughput**
- Maximum sustained throughput
- Tests various frame/buffer size combinations
- Identifies optimal configurations

**BM_BurstThroughput**
- How fast can we fill the buffer
- Tests burst write performance
- No reader backpressure

**BM_MemoryPressureThroughput**
- Performance with small buffers
- Simulates memory-constrained environments
- Shows impact of frequent blocking

### 3. Scenario Measurements (`benchmark_scenarios`)

**BM_WrapAroundOverhead**
- Cost of circular buffer wrap-around
- Tests with different frame sizes
- Identifies wrap-around penalties

**BM_MetadataOperations**
- Metadata read/write performance
- Tests various metadata sizes
- One-time operation cost

**BM_RandomAccessPattern**
- Real-world variable frame sizes
- Random delays to simulate processing
- Overall system behavior

**BM_ConnectionSetupTeardown**
- Cost of establishing connections
- Important for short-lived connections
- Includes resource allocation

## Running Specific Benchmarks

```bash
# Run single benchmark
./benchmarks/benchmark_latency --benchmark_filter=BM_SingleFrameLatency/1024

# Run with detailed statistics
./benchmarks/benchmark_throughput --benchmark_repetitions=10 --benchmark_report_aggregates_only=true

# Run for specific duration
./benchmarks/benchmark_latency --benchmark_min_time=10s

# Output to file
./benchmarks/benchmark_scenarios --benchmark_out=results.json --benchmark_out_format=json
```

## Interpreting Results

### Sample Output
```
BM_SingleFrameLatency/1024/manual_time    12.5 us    15.2 us    56234 bytes_per_second=78.1Mi/s
```

- **12.5 us**: Wall-clock time per iteration
- **15.2 us**: CPU time per iteration  
- **56234**: Number of iterations
- **78.1Mi/s**: Calculated throughput

### Key Metrics

**Latency**: Look for
- Mean/median for typical performance
- StdDev for consistency
- Min for best-case
- Max for worst-case spikes

**Throughput**: Look for
- bytes_per_second for data rate
- items_per_second for frame rate
- CPU utilization (CPU time vs real time)

## Performance Tuning

### For Low Latency
```cpp
BufferConfig config(0, 64 * 1024);     // Small 64KB buffer
// Pin to same CPU core
// Disable debug logging
// Use Release build
```

### For High Throughput  
```cpp
BufferConfig config(0, 100 * 1024 * 1024);  // Large 100MB buffer
// Use 4-64KB frames
// Separate CPU cores
// Enable CPU performance mode
```

### System Tuning
```bash
# Disable CPU frequency scaling
sudo cpupower frequency-set -g performance

# Increase shared memory limits
sudo sysctl -w kernel.shmmax=2147483648

# Pin to specific CPUs
taskset -c 0 ./reader & taskset -c 1 ./writer
```

## Benchmark Development

### Adding New Benchmarks

```cpp
static void BM_YourBenchmark(benchmark::State& state) {
    // Setup (not timed)
    size_t param = state.range(0);
    
    for (auto _ : state) {
        // Timed section
        DoOperation(param);
    }
    
    // Report metrics
    state.SetBytesProcessed(state.iterations() * param);
}

BENCHMARK(BM_YourBenchmark)
    ->Args({1024})      // Test with 1KB
    ->Args({1048576})   // Test with 1MB
    ->Unit(benchmark::kMicrosecond);
```

### Manual Timing

```cpp
for (auto _ : state) {
    state.PauseTiming();
    // Setup not included in timing
    
    state.ResumeTiming();
    // Only this is timed
    
    state.PauseTiming();
    // Cleanup not included
}
```

## Continuous Performance Testing

### Regression Detection
```bash
# Save baseline
./benchmarks/benchmark_all --benchmark_out=baseline.json

# Compare with new results  
./benchmarks/benchmark_all --benchmark_out=new.json
compare_benchmarks.py baseline.json new.json
```

### Performance CI
```yaml
# Example GitHub Actions
- name: Run Benchmarks
  run: |
    ./build_release.sh
    ./benchmarks/benchmark_latency --benchmark_out=results.json
    
- name: Store Results
  uses: benchmark-action/github-action-benchmark@v1
```

## Troubleshooting

### High Variance
- Check for other processes
- Disable CPU throttling
- Use `--benchmark_repetitions=20`
- Check for system interrupts

### Unexpected Results
- Verify Release build
- Check buffer sizes
- Monitor system resources
- Enable performance counters

### Debugging Performance
```bash
# Profile with perf
perf record ./benchmarks/benchmark_throughput
perf report

# Check cache misses
perf stat -e cache-misses,cache-references ./benchmark

# Monitor system calls
strace -c ./benchmarks/benchmark_latency
```