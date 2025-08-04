# ZeroBuffer Cross-Platform Tests

This directory contains tests to verify interoperability between different language implementations of ZeroBuffer.

## Directory Structure

```
cross-platform-tests/
├── round-trip/           # Round-trip tests between implementations
│   ├── cpp-csharp/      # C++ ↔ C# tests ✅
│   ├── cpp-python/      # C++ ↔ Python tests  
│   └── csharp-python/   # C# ↔ Python tests
├── compatibility/        # Protocol compatibility tests
└── relay/               # Relay/chain tests
```

## Test Status

### C++ ↔ C# Tests ✅
All tests passing:
- C++ → C# direct transfer
- C# → C++ direct transfer  
- C++ → C# relay → C++ round-trip

Key fixes implemented:
- Added `is_writer_connected(timeout)` to both implementations
- Fixed wrap-around protocol (using `payload_size=0` as marker)
- Added comprehensive debug logging

## Test Programs

### C++ Test Programs
Located in `cpp/tests/cross-platform/`:
- `zerobuffer-test-writer` - Writes frames with configurable patterns
- `zerobuffer-test-reader` - Reads and verifies frames

### C# Test Programs  
Located in `csharp/ZeroBuffer.CrossPlatform/`:
- `ZeroBuffer.CrossPlatform writer` - Writes frames
- `ZeroBuffer.CrossPlatform reader` - Reads frames
- `ZeroBuffer.CrossPlatform relay` - Relays between buffers

## Running Tests

Navigate to the specific test directory and run the test scripts:

```bash
cd round-trip/cpp-csharp
./test_cpp_to_csharp.sh   # C++ writer → C# reader
./test_csharp_to_cpp.sh   # C# writer → C++ reader
./test_with_relay.sh      # C++ → C# relay → C++
```

## Test Configuration

All tests use consistent parameters:
- Frame size: 3.1MB (YUV420 1920x1080)
- Buffer size: 256MB
- Pattern: Sequential (for verification)
- FPS: 30 (realistic streaming rate)

This ensures wrap-around behavior is tested (typically occurs at frame 87).

## Expected Results

Successful tests should show:
1. All frames transferred (300 frames)
2. Zero verification errors
3. Wrap-around events logged
4. Exit code 0 for all processes