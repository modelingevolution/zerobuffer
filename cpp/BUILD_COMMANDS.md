# ZeroBuffer Build Commands

## Quick Start

```bash
# Build (Release mode by default)
./build.sh

# Build in Debug mode
./build.sh Debug

# Clean build (removes previous build)
./build.sh Release clean

# Run all tests
./test.sh

# Run only unit tests
./test.sh unit

# Run only benchmarks
./test.sh benchmark

# Clean everything
./clean.sh
```

## Standard Commands

### Building
- `./build.sh` - Build in Release mode
- `./build.sh Debug` - Build in Debug mode  
- `./build.sh Release clean` - Clean build

### Testing
- `./test.sh` - Run all tests (unit + benchmarks)
- `./test.sh unit` - Run unit tests only
- `./test.sh benchmark` - Run benchmarks only

### Cleaning
- `./clean.sh` - Remove all build artifacts and shared memory resources

## Manual Commands (if needed)

```bash
# Build manually
mkdir -p build
cd build
cmake .. -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTS=ON -DBUILD_BENCHMARKS=ON
make -j$(nproc)

# Run tests manually
cd build
ctest --output-on-failure

# Run specific benchmark
cd build
./benchmarks/benchmark_latency
./benchmarks/benchmark_throughput
./benchmarks/benchmark_scenarios
./benchmarks/benchmark_video
```

## Important Notes

- The library now uses plain OIEB structure (no std::atomic) for cross-language compatibility
- All tests should pass (31 unit tests)
- Benchmarks may need semaphore cleanup between runs if they fail