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
struct ProtocolVersion {
    uint8_t major;     // Major version (breaking changes)
    uint8_t minor;     // Minor version (new features, backward compatible)
    uint8_t patch;     // Patch version (bug fixes)
    uint8_t reserved;  // Reserved (must be 0)
};

struct OIEB {
    uint32_t oieb_size;           // Total OIEB size (always 128 for v1)
    ProtocolVersion version;      // Protocol version (currently 1.0.0.0)
    
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
    uint64_t reserved[4];         // Reserved for future use
};
```

Note: The first 8 bytes contain oieb_size (4B) and version (4B), maintaining 128-byte total size.

## Key Commands

**IMPORTANT**: NEVER invoke `make` directly in this project! Always use the provided build scripts.

```bash
# Building
./build.sh              # Build in Release mode (default)
./build.sh Debug        # Build in Debug mode
./build.sh Release clean # Clean build

# Note: Initial builds can take up to 5 minutes due to dependency downloads
# (Google Test, nlohmann/json, etc.). Subsequent builds are much faster.

# Testing
./test.sh               # Run all tests (unit + benchmarks)
./test.sh unit          # Run unit tests only
./test.sh benchmark     # Run benchmarks only
./test.sh 1.1           # Run specific test by number (e.g., test 1.1)
./test.sh "1.*"         # Run all tests in category 1 (wildcard support)
./test.sh "13.*"        # Run all duplex channel tests (category 13)

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

### Performance-Critical Library Guidelines

**CRITICAL**: ZeroBuffer is a performance-critical library. Performance takes precedence over some traditional best practices.

#### Performance Requirements
1. **Zero-copy operations**: No unnecessary memory allocations or copies in hot paths
2. **No virtual function calls in data paths**: Virtual functions only in interfaces, never in read/write operations
3. **Inline critical methods**: Hot path methods should be inline (defined in headers or class body)
4. **Direct member access**: Use `friend class` for zero-overhead access instead of getters/setters in performance paths
5. **Minimize function call overhead**: Avoid unnecessary indirection or wrapper functions
6. **Use POD types**: Plain Old Data structures for shared memory compatibility
7. **Cache-friendly layouts**: Align data structures to cache lines where beneficial

#### C++ Best Practices for Performance-Critical Code
1. **Const-correctness WITHOUT overhead**: Use `const` appropriately but avoid getter/setter overhead
2. **RAII for resource management**: Automatic cleanup without runtime cost
3. **Move semantics**: Prevent unnecessary copies
4. **`noexcept` specifications**: Help compiler optimize better
5. **`constexpr` where possible**: Enable compile-time optimization
6. **Friend class access**: Acceptable for performance-critical member access
7. **Pimpl idiom**: Good for ABI stability but keep performance-critical members accessible

#### What to Avoid
- **DON'T add getters/setters for performance-critical members**: Use friend class instead
- **DON'T use virtual functions in hot paths**: Keep data operations non-virtual
- **DON'T introduce heap allocations in hot paths**: Use stack or pre-allocated memory
- **DON'T over-abstract**: Simple, direct code is often faster
- **DON'T sacrifice performance for "perfect" encapsulation**: Performance is the priority

### Naming Conventions

#### Class Member Variables
- **Private/Protected fields**: Start with underscore and use snake_case
  - Example: `_buffer_size`, `_metadata_start`, `_is_running`
  - This clearly distinguishes member variables from local variables
  - **Note**: Do not refactor existing code just for naming. Apply when touching the code for other reasons.

#### Other Naming Rules
- **Classes/Structs**: PascalCase (e.g., `SharedMemory`, `DuplexClient`)
- **Functions/Methods**: snake_case (e.g., `read_frame()`, `get_metadata()`)
- **Constants**: UPPER_SNAKE_CASE (e.g., `BLOCK_ALIGNMENT`, `MAX_BUFFER_SIZE`)
- **Namespaces**: lowercase (e.g., `zerobuffer`, `platform`)
- **Template parameters**: PascalCase (e.g., `template<typename T>`)
- **Enums**: PascalCase for type, UPPER_SNAKE_CASE for values

### Logging Best Practices
- **ALWAYS use proper logging** instead of `std::cout` or `std::cerr` for debugging
- Use the ZEROBUFFER_LOG macros (ZEROBUFFER_LOG_DEBUG, ZEROBUFFER_LOG_INFO, etc.)
- Log to appropriate levels:
  - DEBUG: Detailed debugging information
  - INFO: General informational messages
  - WARNING: Warning messages
  - ERROR: Error conditions
- Logging is controlled by ZEROBUFFER_LOG_LEVEL environment variable
- Logs go to stderr to avoid interfering with stdout protocol communication

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

### Slow Initial Build
- First build can take up to 5 minutes
- CMake downloads dependencies (Google Test, nlohmann/json)
- Network speed affects download time
- Subsequent builds are much faster (usually under 30 seconds)
- If build seems stuck, wait at least 5 minutes before cancelling

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

## Code Quality Rules

### Best Practices
**IMPORTANT**: Always follow established best practices for the language and framework being used:

1. **Remove redundant code**: Eliminate duplicate fields, unnecessary parameters, and dead code
2. **Use meaningful names**: Choose clear, descriptive names that convey intent
3. **Keep it simple**: Prefer straightforward solutions over complex ones
4. **Follow existing patterns**: Match the codebase's established conventions and style
5. **Validate inputs**: Check parameters and handle edge cases appropriately
6. **Clean up resources**: Ensure proper disposal of resources and cleanup in error paths

### SOLID Principles
Follow SOLID principles for maintainable, extensible code:

1. **Single Responsibility Principle (SRP)**: Each class should have one reason to change
2. **Open/Closed Principle (OCP)**: Open for extension, closed for modification
3. **Liskov Substitution Principle (LSP)**: Subtypes must be substitutable for their base types
4. **Interface Segregation Principle (ISP)**: Prefer many specific interfaces over one general-purpose interface
5. **Dependency Inversion Principle (DIP)**: Depend on abstractions, not concretions

### Separation of Concerns (SoC)
Maintain clear separation between different aspects of the system:

1. **Layer separation**: Keep presentation, business logic, and data access layers distinct
2. **Cross-cutting concerns**: Handle logging, error handling, and security separately from business logic
3. **Module boundaries**: Each module should have a well-defined responsibility and interface
4. **Avoid mixing concerns**: Don't combine I/O operations with business logic, keep UI separate from data processing

### JSON-RPC Communication
**IMPORTANT**: Using `dynamic`, `object`, or anonymous types in JSON-RPC communication is **FORBIDDEN**.

Always use strongly-typed classes for:
- Request parameters
- Response types
- All RPC method invocations

Examples:
```csharp
// WRONG - Using dynamic, object, or anonymous types
var response = await jsonRpc.InvokeAsync<dynamic>("executeStep", request);
var response = await jsonRpc.InvokeAsync<object>("executeStep", request);
var request = new { step = stepText };  // Anonymous type

// CORRECT - Using strongly-typed classes
var response = await jsonRpc.InvokeAsync<StepResponse>("executeStep", request);
var response = await jsonRpc.InvokeAsync<ExecutionResult>("executeStep", request);
var request = new StepRequest { Step = stepText };  // Strongly-typed class
```

This rule applies to all languages (C#, Python, etc.) and ensures:
- Type safety and compile-time checking
- Better IDE support and IntelliSense
- Clear API contracts
- Easier debugging and maintenance