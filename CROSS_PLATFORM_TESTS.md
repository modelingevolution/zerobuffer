# Cross-Platform Interoperability Tests

This document describes the cross-platform testing strategy for ZeroBuffer implementations across C++, C#, and Python. Each language pair is tested in both directions to ensure full compatibility.

## Test Matrix

| Writer | Reader | Test Suite |
|--------|---------|-----------|
| C++ | C# | `cpp_to_csharp` |
| C# | C++ | `csharp_to_cpp` |
| C++ | Python | `cpp_to_python` |
| Python | C++ | `python_to_cpp` |
| C# | Python | `csharp_to_python` |
| Python | C# | `python_to_csharp` |

## Test Scenarios

### 1. Round-Trip Tests

Each language pair performs a round-trip test where:
- Writer creates buffer and writes N frames
- Reader reads all frames and validates
- Measure: End-to-end latency, throughput

#### Test Parameters
- Frame sizes: 1KB, 10KB, 100KB, 1MB
- Frame counts: 100, 1000, 10000
- Buffer sizes: 10MB, 100MB
- Metadata sizes: 1KB, 10KB

### 2. Relay Tests

Three-process relay chain testing:
- Process A (Writer) → Process B (Relay) → Process C (Reader)
- Each process can be in a different language
- Measure: Relay latency, throughput degradation

#### Relay Combinations
1. C++ → C# → Python
2. C++ → Python → C#
3. C# → C++ → Python
4. C# → Python → C++
5. Python → C++ → C#
6. Python → C# → C++

### 3. Specific Compatibility Tests

#### 3.1 Buffer Creation/Discovery
- C++ creates buffer → C#/Python connects
- C# creates buffer → C++/Python connects
- Python creates buffer → C++/C# connects

#### 3.2 Metadata Handling
- Write metadata in one language, read in another
- Verify byte-perfect transmission
- Test various metadata sizes

#### 3.3 Wrap-Around Behavior
- Fill buffer near capacity in one language
- Continue writing/reading in another language
- Verify wrap marker handling is consistent

#### 3.4 Resource Cleanup
- Crash writer in one language
- Verify reader in another language detects it
- Test cleanup of resources across platforms

#### 3.5 Semaphore Compatibility
- Verify POSIX semaphore usage is consistent
- Test timeout behavior across platforms
- Verify signal/wait patterns match

## Test Application Interface Specification

### Unified Command-Line Interface

All test applications across C++, C#, and Python must support the same arguments and behavior to ensure interoperability testing.

#### Test Writer Application

**Purpose**: Write frames to a ZeroBuffer for testing purposes.

**Command**: `zerobuffer-test-writer`

**Arguments**:
```
zerobuffer-test-writer <buffer-name> [options]

Required:
  buffer-name              Name of the buffer to write to

Options:
  --frames, -n COUNT      Number of frames to write (default: 1000)
  --size, -s BYTES        Size of each frame in bytes (default: 1024)
  --metadata, -m TEXT     Metadata to write (optional)
  --metadata-file FILE    Read metadata from file (optional)
  --pattern PATTERN       Data pattern: sequential|random|zero|ones (default: sequential)
  --delay-ms MILLIS       Delay between frames in milliseconds (default: 0)
  --batch-size COUNT      Write frames in batches (default: 1)
  --json-output           Output results in JSON format
  --verbose, -v           Verbose output
  --help, -h              Show help message
```

**Exit Codes**:
- 0: Success - all frames written
- 1: Buffer connection failed
- 2: Write error
- 3: Invalid arguments

#### Test Reader Application

**Purpose**: Read frames from a ZeroBuffer for testing purposes.

**Command**: `zerobuffer-test-reader`

**Arguments**:
```
zerobuffer-test-reader <buffer-name> [options]

Required:
  buffer-name              Name of the buffer to read from

Options:
  --frames, -n COUNT      Number of frames to read (default: 1000, 0 for unlimited)
  --size, -s BYTES        Expected size of each frame in bytes (default: 1024)
  --timeout-ms MILLIS     Timeout per frame in milliseconds (default: 5000)
  --verify PATTERN        Verify data pattern: none|sequential|random|zero|ones (default: none)
  --checksum              Calculate checksums for each frame
  --batch-size COUNT      Read frames in batches (default: 1)
  --json-output           Output results in JSON format
  --verbose, -v           Verbose output
  --help, -h              Show help message
```

**Exit Codes**:
- 0: Success - all expected frames read
- 1: Buffer connection failed
- 2: Read error or timeout
- 3: Invalid arguments
- 4: Validation error (sequence or pattern)

#### Test Relay Application

**Purpose**: Read from one buffer and write to another, acting as a relay.

**Command**: `zerobuffer-test-relay`

**Arguments**:
```
zerobuffer-test-relay <input-buffer> <output-buffer> [options]

Required:
  input-buffer            Name of the buffer to read from
  output-buffer           Name of the buffer to write to

Options:
  --frames, -n COUNT      Number of frames to relay (default: 0 for unlimited)
  --create-output         Create output buffer if it doesn't exist
  --buffer-size BYTES     Output buffer size when creating (default: 256MB)
  --timeout-ms MILLIS     Timeout per frame in milliseconds (default: 5000)
  --transform TRANSFORM   Apply transformation: none|reverse|xor (default: none)
  --xor-key BYTE          XOR key for transform (default: 255)
  --log-interval COUNT    Log progress every N frames (default: 100)
  --json-output           Output results in JSON format
  --verbose, -v           Verbose output
  --help, -h              Show help message
```

**Exit Codes**:
- 0: Success - relay completed
- 1: Input buffer connection failed
- 2: Output buffer connection failed
- 3: Relay error
- 4: Invalid arguments

### Output Format (JSON)

All test applications output standardized JSON when `--json-output` is specified:

**Writer Output**:
```json
{
  "operation": "write",
  "buffer_name": "test_buffer",
  "frames_written": 1000,
  "frame_size": 1024,
  "metadata_size": 256,
  "duration_seconds": 1.234,
  "throughput_mbps": 812.5,
  "errors": []
}
```

**Reader Output**:
```json
{
  "operation": "read",
  "buffer_name": "test_buffer",
  "frames_read": 1000,
  "frame_size": 1024,
  "metadata_size": 256,
  "duration_seconds": 1.234,
  "throughput_mbps": 812.5,
  "verification_errors": 0,
  "errors": []
}
```

**Relay Output**:
```json
{
  "operation": "relay",
  "input_buffer": "buffer_in",
  "output_buffer": "buffer_out",
  "frames_relayed": 1000,
  "metadata_relayed": true,
  "duration_seconds": 1.234,
  "throughput_mbps": 812.5,
  "transform": "none",
  "errors": []
}
```

### Data Pattern Specifications

**Sequential Pattern**: Each byte in frame = (frame_index + byte_index) % 256

**Random Pattern**: Seeded random data (seed = frame_index)

**Zero Pattern**: All bytes = 0x00

**Ones Pattern**: All bytes = 0xFF

## Implementation

### Test Framework Structure

```
cross-platform-tests/
├── round-trip/
│   ├── cpp-csharp/
│   │   ├── run_test.sh
│   │   ├── measure_cpp_to_csharp.sh
│   │   └── measure_csharp_to_cpp.sh
│   ├── cpp-python/
│   │   ├── run_test.sh
│   │   ├── measure_cpp_to_python.sh
│   │   └── measure_python_to_cpp.sh
│   └── csharp-python/
│       ├── run_test.sh
│       ├── measure_csharp_to_python.sh
│       └── measure_python_to_csharp.sh
├── relay/
│   ├── cpp-csharp-python/
│   ├── cpp-python-csharp/
│   ├── csharp-cpp-python/
│   ├── csharp-python-cpp/
│   ├── python-cpp-csharp/
│   └── python-csharp-cpp/
├── compatibility/
│   ├── buffer_creation/
│   ├── metadata/
│   ├── wrap_around/
│   ├── resource_cleanup/
│   └── semaphores/
└── results/
    └── benchmark_results.json
```

### Language-Specific Implementations

#### C++ Implementation

Location: `cpp/tests/cross-platform/`
```bash
# Build
cd cpp && ./build.sh

# Executables
./build/tests/zerobuffer-test-writer
./build/tests/zerobuffer-test-reader
# Note: C++ doesn't have a general-purpose relay test app (only benchmark relay)
# Use ./build/benchmarks/relay_process for benchmarking relay scenarios
```

#### C# Implementation

Location: `csharp/ZeroBuffer.CrossPlatform/`
```bash
# Build
cd csharp && dotnet build

# Run
dotnet run --project ZeroBuffer.CrossPlatform -- writer <args>
dotnet run --project ZeroBuffer.CrossPlatform -- reader <args>
dotnet run --project ZeroBuffer.CrossPlatform -- relay <args>
```

#### Python Implementation

Location: `python/zerobuffer/cross_platform/`
```bash
# Install
cd python && pip install -e .

# Run
python -m zerobuffer.cross_platform.writer <args>
python -m zerobuffer.cross_platform.reader <args>
python -m zerobuffer.cross_platform.relay <args>
```

### Round-Trip Test Script Example

#### `measure_cpp_to_csharp.sh`
```bash
#!/bin/bash
BUFFER_NAME="cross_test_$$"
FRAME_SIZE=$1
FRAME_COUNT=$2
BUFFER_SIZE=$3

# Start C# reader in background
dotnet run --project ../../../csharp/ZeroBuffer.TestHelper -- \
    reader $BUFFER_NAME --measure > reader_metrics.txt &
READER_PID=$!

sleep 1

# Run C++ writer
../../../cpp/build/benchmarks/benchmark_roundtrip \
    --mode writer \
    --buffer $BUFFER_NAME \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --buffer-size $BUFFER_SIZE > writer_metrics.txt

# Wait for reader to finish
wait $READER_PID

# Parse and combine metrics
python3 ../parse_metrics.py writer_metrics.txt reader_metrics.txt
```

### Relay Test Script Example

#### `relay_cpp_csharp_python.sh`
```bash
#!/bin/bash
BUFFER1="relay_in_$$"
BUFFER2="relay_out_$$"

# Start Python reader (final destination)
python3 ../../../python/examples/benchmark_reader.py \
    --buffer $BUFFER2 --measure > reader_metrics.txt &
READER_PID=$!

sleep 1

# Start C# relay
dotnet run --project ../../../csharp/ZeroBuffer.TestHelper -- \
    relay $BUFFER1 $BUFFER2 > relay_metrics.txt &
RELAY_PID=$!

sleep 1

# Run C++ writer
../../../cpp/build/benchmarks/benchmark_roundtrip \
    --mode writer \
    --buffer $BUFFER1 \
    --frames 1000 > writer_metrics.txt

# Cleanup
wait $READER_PID
kill $RELAY_PID 2>/dev/null
```

## Metrics Collection

### Performance Metrics
- **Throughput**: MB/s for each language pair
- **Latency**: Average, P50, P95, P99 frame latency
- **CPU Usage**: Per-process CPU utilization
- **Memory Usage**: Shared memory overhead

### Compatibility Metrics
- **Success Rate**: Percentage of successful cross-platform operations
- **Error Types**: Classification of any failures
- **Resource Leaks**: Detection of orphaned resources

### Output Format
```json
{
  "test": "round_trip",
  "writer": "cpp",
  "reader": "csharp",
  "parameters": {
    "frame_size": 1024,
    "frame_count": 1000,
    "buffer_size": 10485760
  },
  "results": {
    "throughput_mbps": 1250.5,
    "latency_us": {
      "avg": 125,
      "p50": 110,
      "p95": 180,
      "p99": 250
    },
    "cpu_percent": {
      "writer": 15.2,
      "reader": 12.8
    },
    "success": true,
    "errors": []
  }
}
```

## Continuous Integration

### GitHub Actions Workflow
```yaml
name: Cross-Platform Tests

on: [push, pull_request]

jobs:
  cross-platform:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Build C++
      run: |
        cd cpp && ./build.sh
    
    - name: Build C#
      run: |
        cd csharp && dotnet build
    
    - name: Setup Python
      run: |
        cd python && pip install -e .
    
    - name: Run Cross-Platform Tests
      run: |
        cd cross-platform-tests
        ./run_all_tests.sh
    
    - name: Upload Results
      uses: actions/upload-artifact@v3
      with:
        name: cross-platform-results-${{ matrix.os }}
        path: cross-platform-tests/results/
```

## Known Compatibility Considerations

### 1. Shared Memory Naming
- C++/Python: Direct POSIX names (e.g., `/shm_name`)
- C#: May need platform-specific handling
- Windows: Different shared memory mechanisms

### 2. Semaphore Behavior
- POSIX semaphores on Linux/macOS
- Named semaphores compatibility
- Windows: Different semaphore implementation

### 3. Memory Alignment
- All platforms must respect 64-byte alignment
- Structure packing must be consistent
- Endianness considerations (if supporting different architectures)

### 4. Process ID Handling
- PID representation across platforms
- Process existence checking methods
- Cleanup timing differences

## Test Execution

### Running All Tests
```bash
cd cross-platform-tests
./run_all_tests.sh
```

### Running Specific Test Suite
```bash
# Round-trip C++ to C#
./round-trip/cpp-csharp/run_test.sh

# Relay chain test
./relay/cpp-csharp-python/run_test.sh

# Compatibility tests
./compatibility/run_all.sh
```

### Viewing Results
```bash
# Generate HTML report
python3 generate_report.py results/ > report.html

# View summary
python3 summarize_results.py results/
```

## Troubleshooting

### Common Issues

1. **Semaphore timeouts**
   - Check semaphore name format compatibility
   - Verify initial count matches across platforms

2. **Shared memory access**
   - Ensure proper permissions
   - Check memory alignment

3. **Resource cleanup**
   - Platform-specific cleanup timing
   - Lock file compatibility

### Debug Mode
```bash
# Enable debug logging
export ZEROBUFFER_DEBUG=1
./run_test.sh
```

## Future Enhancements

1. **Automated Performance Regression Detection**
   - Track performance over time
   - Alert on significant degradation

2. **Stress Testing**
   - Long-running cross-platform tests
   - Random failure injection

3. **Platform-Specific Optimizations**
   - Measure impact of platform-specific code
   - Ensure optimizations don't break compatibility

4. **Additional Language Bindings**
   - Rust implementation testing
   - Go implementation testing
   - Java/JNI implementation testing