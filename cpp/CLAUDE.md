# CLAUDE.md - ZeroBuffer Project

This file provides guidance to Claude Code when working with the ZeroBuffer library.

## Project Overview

ZeroBuffer is a high-performance, zero-copy inter-process communication library designed for video streaming applications. It provides shared memory buffers with metadata support and proper resource cleanup.

### Key Features
- Zero-copy shared memory buffers
- Cross-process synchronization with semaphores
- Automatic resource cleanup and stale process detection
- Buffer wrap-around support for continuous streaming
- Metadata attachment to buffers
- Cross-language compatible protocol (plain POD structures)

## Architecture

The library uses a plain POD (Plain Old Data) structure called OIEB (Operation Info Exchange Block) for cross-language compatibility:

```cpp
struct OIEB {
    uint64_t operation_size;      // Total OIEB size
    uint64_t metadata_size;       // Total metadata block size
    uint64_t metadata_free_bytes; // Free bytes in metadata block
    uint64_t metadata_written_bytes; // Written bytes in metadata block
    uint64_t payload_size;        // Total payload block size
    uint64_t payload_free_bytes;  // Free bytes in payload block
    uint64_t payload_write_pos;   // Current write position in buffer
    uint64_t payload_read_pos;    // Current read position in buffer
    uint64_t payload_written_count; // Number of frames written
    uint64_t payload_read_count;   // Number of frames read
    uint64_t writer_pid;          // Writer process ID (0 if none)
    uint64_t reader_pid;          // Reader process ID (0 if none)
    uint64_t reserved[4];         // Padding to ensure 64-byte alignment
};
```

## Key Commands

```bash
# Building
./build.sh              # Build in Release mode (default)
./build.sh Debug        # Build in Debug mode
./build.sh Release clean # Clean build

# Testing
./test.sh               # Run all tests (unit + benchmarks)
./test.sh unit          # Run unit tests only
./test.sh benchmark     # Run benchmarks only

# Cleaning
./clean.sh              # Clean all build artifacts and shared memory resources
```

## Manual Build Commands

```bash
# Create build directory and configure
mkdir -p build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTS=ON -DBUILD_BENCHMARKS=ON
make -j$(nproc)

# Run tests
ctest --output-on-failure

# Run specific test suite
./tests/test_zerobuffer
./tests/test_resource_cleanup
./tests/test_scenarios
```

## Development Guidelines

### Adding New Tests
- Add test files to `tests/` directory
- Update `tests/CMakeLists.txt` to include new test executables
- Use Google Test framework
- Follow existing test patterns for resource cleanup

### Resource Management
- Reader creates and owns the shared memory buffer
- Writer connects to existing buffer
- Resources are cleaned up when both Reader and Writer are destroyed
- Stale resource detection handles abnormal termination

### Cross-Language Implementation
- Use only POD types in shared structures
- Avoid C++ specific features (std::atomic, virtual functions, etc.)
- Document exact byte layout and alignment requirements
- Test with different compilers and platforms

## Common Issues

### Clock Skew Warnings
- Common in WSL environments
- Usually harmless but can affect build timestamps
- Run `./clean.sh` if builds behave unexpectedly

### Semaphore Cleanup
- Benchmarks may fail with "Failed to open semaphore"
- Run `./clean.sh` between benchmark runs
- This cleans up stale semaphores from previous runs

### Buffer Name Reuse
- After proper cleanup, buffer names can be reused
- The library handles stale resource cleanup automatically
- Tests verify this behavior extensively

## Testing

The project includes comprehensive tests:
- **Unit tests**: 35 tests covering core functionality
- **Resource cleanup tests**: 12 tests for cleanup and name reuse scenarios
- **Scenario tests**: Real-world usage patterns
- **Benchmarks**: Performance measurements for latency and throughput

All tests should pass with 100% success rate.