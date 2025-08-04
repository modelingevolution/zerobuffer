# Cross-Platform Test Application Interface Specification

This document defines the unified command-line interface for ZeroBuffer test applications across C++, C#, and Python. All implementations must support the same arguments and behavior to ensure interoperability testing.

## Test Applications

### 1. Test Writer Application

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

**Output Format (JSON)**:
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

**Exit Codes**:
- 0: Success - all frames written
- 1: Buffer connection failed
- 2: Write error
- 3: Invalid arguments

### 2. Test Reader Application

**Purpose**: Read frames from a ZeroBuffer for testing purposes.

**Command**: `zerobuffer-test-reader`

**Arguments**:
```
zerobuffer-test-reader <buffer-name> [options]

Required:
  buffer-name              Name of the buffer to read from

Options:
  --frames, -n COUNT      Expected number of frames (default: unlimited)
  --create                Create buffer if it doesn't exist
  --buffer-size BYTES     Buffer size when creating (default: 10MB)
  --metadata-size BYTES   Metadata size when creating (default: 1KB)
  --timeout-ms MILLIS     Timeout per frame in milliseconds (default: 5000)
  --validate              Validate frame sequence numbers
  --check-pattern         Validate data pattern
  --json-output           Output results in JSON format
  --verbose, -v           Verbose output
  --help, -h              Show help message
```

**Output Format (JSON)**:
```json
{
  "operation": "read",
  "buffer_name": "test_buffer",
  "frames_read": 1000,
  "metadata_size": 256,
  "duration_seconds": 1.234,
  "throughput_mbps": 812.5,
  "average_latency_us": 125.6,
  "sequence_errors": 0,
  "pattern_errors": 0,
  "errors": []
}
```

**Exit Codes**:
- 0: Success - all expected frames read
- 1: Buffer connection failed
- 2: Read error or timeout
- 3: Invalid arguments
- 4: Validation error (sequence or pattern)

### 3. Test Relay Application

**Purpose**: Read from one buffer and write to another, acting as a relay.

**Command**: `zerobuffer-test-relay`

**Arguments**:
```
zerobuffer-test-relay <input-buffer> <output-buffer> [options]

Required:
  input-buffer            Name of the buffer to read from
  output-buffer           Name of the buffer to write to

Options:
  --frames, -n COUNT      Number of frames to relay (default: unlimited)
  --create-output         Create output buffer if it doesn't exist
  --buffer-size BYTES     Output buffer size when creating (default: same as input)
  --timeout-ms MILLIS     Timeout per frame in milliseconds (default: 5000)
  --transform TRANSFORM   Apply transformation: none|reverse|xor (default: none)
  --xor-key BYTE          XOR key for transform (default: 0xFF)
  --json-output           Output results in JSON format
  --verbose, -v           Verbose output
  --help, -h              Show help message
```

**Output Format (JSON)**:
```json
{
  "operation": "relay",
  "input_buffer": "buffer_in",
  "output_buffer": "buffer_out",
  "frames_relayed": 1000,
  "duration_seconds": 1.234,
  "throughput_mbps": 812.5,
  "average_latency_us": 250.3,
  "transform": "none",
  "errors": []
}
```

**Exit Codes**:
- 0: Success - relay completed
- 1: Input buffer connection failed
- 2: Output buffer connection failed
- 3: Relay error
- 4: Invalid arguments

## Implementation Requirements

### 1. Consistent Behavior

All implementations must:
- Use the same default values
- Support all command-line options
- Output the same JSON format when requested
- Use the same exit codes
- Handle errors consistently

### 2. Data Patterns

**Sequential Pattern**: Each byte in frame = (frame_index + byte_index) % 256

**Random Pattern**: Seeded random data (seed = frame_index)

**Zero Pattern**: All bytes = 0x00

**Ones Pattern**: All bytes = 0xFF

### 3. Validation

**Sequence Validation**: Check that frame sequence numbers are consecutive starting from 1

**Pattern Validation**: Verify data matches the expected pattern based on frame index

### 4. Performance Measurement

All applications must measure and report:
- Total duration
- Throughput (MB/s)
- Average latency (microseconds) - time between operations
- Frame rate (frames/second)

## Language-Specific Implementations

### C++ Implementation

Location: `cpp/tests/cross-platform/`
```bash
# Build
cd cpp && ./build.sh

# Executables
./build/tests/cross-platform/zerobuffer-test-writer
./build/tests/cross-platform/zerobuffer-test-reader
./build/tests/cross-platform/zerobuffer-test-relay
```

### C# Implementation

Location: `csharp/ZeroBuffer.CrossPlatform/`
```bash
# Build
cd csharp && dotnet build

# Run
dotnet run --project ZeroBuffer.CrossPlatform -- writer <args>
dotnet run --project ZeroBuffer.CrossPlatform -- reader <args>
dotnet run --project ZeroBuffer.CrossPlatform -- relay <args>
```

### Python Implementation

Location: `python/zerobuffer/cross_platform/`
```bash
# Install
cd python && pip install -e .

# Run
python -m zerobuffer.cross_platform.writer <args>
python -m zerobuffer.cross_platform.reader <args>
python -m zerobuffer.cross_platform.relay <args>
```

## Test Script Examples

### Round-Trip Test
```bash
#!/bin/bash
# Test C++ writer to Python reader

BUFFER="test_$$"
FRAMES=1000
SIZE=1024

# Start reader
python -m zerobuffer.cross_platform.reader $BUFFER \
    --create --frames $FRAMES --validate --json-output > reader.json &
READER_PID=$!

sleep 1

# Run writer
./zerobuffer-test-writer $BUFFER \
    --frames $FRAMES --size $SIZE --json-output > writer.json

# Wait for reader
wait $READER_PID

# Parse results
WRITER_THROUGHPUT=$(jq .throughput_mbps writer.json)
READER_THROUGHPUT=$(jq .throughput_mbps reader.json)
echo "Writer: $WRITER_THROUGHPUT MB/s, Reader: $READER_THROUGHPUT MB/s"
```

### Relay Chain Test
```bash
#!/bin/bash
# Test C++ → C# → Python relay chain

BUFFER1="relay_in_$$"
BUFFER2="relay_out_$$"
FRAMES=1000

# Start final reader (Python)
python -m zerobuffer.cross_platform.reader $BUFFER2 \
    --create --frames $FRAMES --json-output > reader.json &
READER_PID=$!

sleep 1

# Start relay (C#)
dotnet run --project ZeroBuffer.CrossPlatform -- relay \
    $BUFFER1 $BUFFER2 --create-output --json-output > relay.json &
RELAY_PID=$!

sleep 1

# Run writer (C++)
./zerobuffer-test-writer $BUFFER1 \
    --frames $FRAMES --json-output > writer.json

# Wait for completion
wait $READER_PID
kill $RELAY_PID 2>/dev/null
```

## Validation Matrix

Each test run should validate:

1. **Frame Count**: Frames written = Frames read
2. **Data Integrity**: Pattern validation passes
3. **Sequence Numbers**: No gaps or duplicates
4. **Metadata**: Correctly transmitted (if provided)
5. **Performance**: Throughput within expected range
6. **Error Handling**: Proper error codes on failure

## JSON Schema

### Common Fields
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["operation", "buffer_name", "duration_seconds", "errors"],
  "properties": {
    "operation": {
      "type": "string",
      "enum": ["write", "read", "relay"]
    },
    "buffer_name": {
      "type": "string"
    },
    "duration_seconds": {
      "type": "number",
      "minimum": 0
    },
    "throughput_mbps": {
      "type": "number",
      "minimum": 0
    },
    "errors": {
      "type": "array",
      "items": {
        "type": "string"
      }
    }
  }
}
```

## Environment Variables

All test applications should respect:

- `ZEROBUFFER_DEBUG`: Enable debug output (0/1)
- `ZEROBUFFER_JSON_PRETTY`: Pretty-print JSON output (0/1)
- `ZEROBUFFER_TEMP_DIR`: Directory for temporary files

## Future Extensions

1. **Compression Testing**: Add compression/decompression in relay
2. **Multi-threaded Testing**: Support concurrent readers/writers
3. **Network Testing**: Add TCP/UDP relay options
4. **Fault Injection**: Simulate errors for robustness testing
5. **Performance Profiling**: Detailed timing breakdowns