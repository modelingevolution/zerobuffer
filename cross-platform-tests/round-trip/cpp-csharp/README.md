# C++ ↔ C# Round-Trip Tests

This directory contains round-trip tests between C++ and C# implementations of ZeroBuffer.

## Test Scripts

- `test_cpp_to_csharp.sh` - Tests C++ writer → C# reader ✅
- `test_csharp_to_cpp.sh` - Tests C# writer → C++ reader ✅
- `test_with_relay.sh` - Tests C++ → C# relay → C++ ✅
- `test_bidirectional.sh` - Runs all bidirectional tests

## Running Tests

```bash
# Run individual tests
./test_cpp_to_csharp.sh      # C++ writes, C# reads
./test_csharp_to_cpp.sh      # C# writes, C++ reads
./test_with_relay.sh         # Full round-trip through relay

# Run all bidirectional tests
./test_bidirectional.sh
```

## Key Features Tested

- **Cross-language compatibility**: Binary protocol works between C++ and C#
- **Wrap-around handling**: Proper circular buffer management (typically at frame 87)
- **Writer connection timeout**: Reader waits up to 30 seconds for writer
- **Data integrity**: Sequential pattern verification
- **Performance**: ~60-80 MB/s direct, ~55 MB/s through relay

## Test Parameters

All tests use:
- Frame size: 3.1MB (YUV420 1920x1080)
- Buffer size: 256MB
- Frame count: 300 (ensures wrap-around)
- Pattern: Sequential (for verification)
- FPS: 30 (realistic streaming rate)

## Expected Results

Each test should:
1. Transfer all frames successfully
2. Verify data integrity (0 errors)
3. Demonstrate wrap-around handling (typically at frame 87)
4. Show debug logging for wrap-around events