# ZeroBuffer Project Structure

## Overview
ZeroBuffer is a protocol-based inter-process communication library with implementations in multiple languages.

## Directory Structure

```
zerobuffer/
├── README.md                    # Protocol specification
├── PROTOCOL.md                  # High-level protocol overview
├── TEST_SCENARIOS.md            # Comprehensive test scenarios
├── README_PROJECT_STRUCTURE.md  # This file
│
├── cpp/                         # C++ Implementation
│   ├── README.md               # C++ specific documentation
│   ├── CMakeLists.txt          # CMake build configuration
│   ├── build.sh                # Build script
│   ├── test.sh                 # Test runner script
│   ├── include/zerobuffer/     # Public headers
│   │   ├── zerobuffer.h        # Main include file
│   │   ├── types.h             # Core types and structures
│   │   ├── reader.h            # Reader class interface
│   │   ├── writer.h            # Writer class interface
│   │   └── platform.h          # Platform abstraction layer
│   ├── src/                    # Implementation files
│   │   ├── reader.cpp          # Reader implementation
│   │   ├── writer.cpp          # Writer implementation
│   │   ├── platform_linux.cpp  # Linux-specific code
│   │   └── platform_windows.cpp # Windows-specific code
│   ├── tests/                  # Unit tests
│   │   ├── test_zerobuffer.cpp # Basic unit tests
│   │   └── test_scenarios.cpp  # Comprehensive scenario tests
│   └── examples/               # Example applications
│       ├── example_reader.cpp
│       ├── example_writer.cpp
│       └── example_metadata.cpp
│
├── csharp/                     # C# Implementation (in progress)
│   ├── ZeroBuffer/            # Core library
│   ├── ZeroBuffer.Tests/      # In-process tests
│   ├── ZeroBuffer.Benchmarks/ # Performance benchmarks
│   └── README.md              # C# specific documentation
│
└── python/                     # Python Implementation (in progress)
    ├── zerobuffer/            # Core library package
    ├── tests/                 # Unit and integration tests
    ├── benchmarks/            # Performance benchmarks
    └── README.md              # Python specific documentation
```

## Protocol Implementation Status

### C++ Implementation ✓
- Full protocol implementation
- Linux support (POSIX shared memory + semaphores)
- Windows support (Windows API)
- Comprehensive test suite using Google Test
- Zero-copy frame access
- Template-based metadata access
- Process crash detection
- Automatic resource cleanup

### C# Implementation (Planned)
- Will use .NET MemoryMappedFile
- P/Invoke for semaphores on Linux
- Native Windows synchronization primitives

### Python Implementation (Planned)
- Will use multiprocessing.shared_memory
- POSIX semaphores via posix_ipc
- Windows semaphores via pywin32

## Building and Testing

### C++ Implementation
```bash
cd cpp
./build.sh
./test.sh
```

## Key Protocol Features
1. Single writer, single reader model
2. Zero-copy data access
3. Lock-free ring buffer (payload)
4. Write-once metadata
5. Process crash detection (5-second timeout)
6. Automatic stale resource cleanup
7. 64-byte memory alignment
8. Cross-platform support