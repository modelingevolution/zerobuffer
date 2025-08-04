# C++ ↔ C# Round-Trip Tests

This directory contains round-trip tests between C++ and C# implementations of ZeroBuffer.

## Test Scripts

- `test_cpp_to_csharp.sh` - Tests C++ writer → C# reader ✅
- `test_bidirectional.sh` - Runs available round-trip tests

## Running Tests

```bash
# Run C++ → C# test
./test_cpp_to_csharp.sh

# Run all available tests
./test_bidirectional.sh
```

## Known Limitations

The C# → C++ test is not currently supported because the C++ test reader program
exits immediately if no writer is connected. This would require modifying the
C++ reader to add a `--wait-for-writer` flag or similar functionality.

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