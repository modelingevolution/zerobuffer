# ZeroBuffer Protocol Acceptance Test Scenarios

> **Note**: These test scenarios have been translated into Gherkin feature files for use with the SpecFlow-based test runner. See the feature files in:
> - C#: `/csharp/ZeroBuffer.ProtocolTests/Features/`
> - Python: `/python/tests/features/` (when implemented)
> - C++: `/cpp/tests/features/` (when implemented)

This document serves as the reference specification for all test scenarios.

## Test Execution Modes

The feature files support execution in multiple modes through the test runner configuration. Each scenario can be executed with different language implementations for readers and writers.

**IMPORTANT**: All test scenarios listed in this document MUST be executed in two different modes:

### Mode 1: Same Process
- Reader and Writer run in the same process (different threads)
- Tests in-process synchronization and memory barriers
- Faster execution, easier debugging
- Validates thread safety and memory consistency

### Mode 2: Separate Processes  
- Reader and Writer run in separate processes
- Test process should fork() or spawn child processes
- Tests true IPC behavior, process isolation, and crash handling
- Validates cross-process synchronization and shared memory semantics

Each test framework implementation should provide a way to run the entire test suite in both modes. Failures in either mode should be considered test failures.

## Test Architecture Design

### Test Numbering Scheme
Each test scenario MUST be assigned a unique numeric identifier following the section numbering:
- Test 1.1 = Test ID 101
- Test 1.2 = Test ID 102
- Test 1.3 = Test ID 103
- Test 2.1 = Test ID 201
- Test 10.3 = Test ID 1003
- etc.

### Implementation Structure

#### 1. Test Library (Shared Library/DLL)
```
zerobuffer_tests.so / zerobuffer_tests.dll
├── test_101_writer(buffer_name) - Writer side of test 1.1
├── test_101_reader(buffer_name) - Reader side of test 1.1
├── test_102_writer(buffer_name) - Writer side of test 1.2
├── test_102_reader(buffer_name) - Reader side of test 1.2
└── ... (all test scenarios)
```

#### 2. Test Runner Executable
```
zerobuffer_test_runner <mode> <test_id> <role> <buffer_name> [options]
  mode: "same-process" | "separate-process" | "cross-platform"
  test_id: 101, 102, 201, etc.
  role: "writer" | "reader" | "both" (both only for same-process)
  buffer_name: Unique name for this test run
```

#### 3. Helper Runner (for separate process/cross-platform)
```
zerobuffer_test_helper <language> <test_id> <role> <buffer_name>
  language: "cpp" | "csharp" | "python"
  test_id: Numeric test identifier
  role: "writer" | "reader"
  buffer_name: Buffer name to use
```

### Usage Examples

**Same Process Mode:**
```bash
zerobuffer_test_runner same-process 101 both test-buffer-xyz
```

**Separate Process Mode:**
```bash
# Main runner spawns two processes
zerobuffer_test_runner separate-process 101 writer test-buffer-xyz &
zerobuffer_test_runner separate-process 101 reader test-buffer-xyz
```

**Cross-Platform Mode:**
```bash
# C++ writer, C# reader
zerobuffer_test_helper cpp 101 writer test-buffer-xyz &
zerobuffer_test_helper csharp 101 reader test-buffer-xyz
```

This architecture enables:
- Single implementation of each test scenario
- Easy cross-platform testing by mixing languages
- Consistent test behavior across all modes
- Clear test identification and reproduction

## 1. Basic Communication Tests

**Feature File**: `BasicCommunication.feature`

### 1.1 Simple Write-Read Cycle
- Reader creates buffer with name "test-basic" (metadata: 1KB, payload: 10KB)
- Writer connects and writes metadata (100 bytes)
- Writer writes single frame (1KB) with sequence number 1
- Reader reads frame and validates sequence number
- Reader signals space available
- Verify: Frame data integrity, sequence number correctness

### 1.2 Multiple Frames Sequential
- Setup buffer "test-multi" (metadata: 1KB, payload: 100KB)
- Writer writes metadata (200 bytes)
- Writer writes 10 frames (5KB each) with sequence numbers 1-10
- Reader reads all frames one by one
- Verify: All frames received in order, sequence numbers 1-10

### 1.3 Buffer Full Handling
- Setup small buffer "test-full" (metadata: 512B, payload: 10KB)
- Writer writes metadata
- Writer writes 8KB frame (success)
- Writer attempts to write 5KB frame (should block)
- Reader reads first frame and signals
- Writer's second write completes
- Verify: Writer blocks correctly, resumes after space available

## 2. Process Lifecycle Tests

**Feature File**: `ProcessLifecycle.feature`

### 2.1 Writer Crash Detection
- Setup buffer "test-writer-crash"
- Writer connects and writes metadata
- Writer writes frame and records PID in OIEB
- Simulate writer crash (kill process)
- Reader attempts to read, waits on semaphore
- After 5 seconds timeout, reader checks writer PID
- Verify: Reader detects writer process doesn't exist, throws exception

### 2.2 Reader Crash Detection
- Setup buffer "test-reader-crash"
- Reader creates buffer and records PID
- Writer connects and fills buffer completely
- Simulate reader crash (kill process)
- Writer attempts another write (blocks on sem-r)
- After 5 seconds timeout, writer checks reader PID
- Verify: Writer detects reader process doesn't exist, throws exception

### 2.3 Reader Replacement After Crash
- Setup buffer "test-reader-replacement"
- Reader1 creates buffer, writer connects
- Writer writes frames continuously
- Reader1 crashes (PID remains in OIEB)
- New reader2 attempts to create buffer with same name
- Verify: Reader2 detects stale reader1 (PID check fails), cleans up resources, creates fresh buffer
- Verify: Writer detects reader death on next write attempt

## 3. Initialization Tests

**Feature Files**: `Initialization.feature`, `ErrorHandling.feature`

### 3.1 Stale Resource Cleanup
- Manually create lock file "/tmp/zerobuffer/stale-test.lock" (not held by any process)
- Create orphaned shared memory and semaphores for "stale-test"
- New reader attempts to create buffer "stale-test"
- Verify: Reader successfully removes stale resources and creates fresh buffer

### 3.2 Concurrent Initialization Race
- Two readers simultaneously attempt to create buffer "race-test"
- Verify: Only one succeeds, other receives appropriate error

### 3.3 Writer Before Reader
- Writer attempts to connect to non-existent buffer "no-reader"
- Verify: Writer fails with appropriate error (no shared memory found)

## 4. Metadata Tests

**Feature Files**: `ErrorHandling.feature`, `Initialization.feature`

### 4.1 Metadata Write-Once
- Setup buffer "test-metadata-once"
- Writer writes metadata (500 bytes)
- Writer writes several frames
- Writer attempts to write metadata again
- Verify: Second metadata write fails or is rejected

### 4.2 Metadata Size Validation
- Setup buffer "test-metadata-overflow" (metadata: 1KB, payload: 10KB)
- Writer attempts to write 2KB metadata
- Verify: Write fails due to insufficient metadata block size

### 4.3 Zero Metadata
- Setup buffer "test-no-metadata"
- Writer connects but writes no metadata (metadata size = 0)
- Writer writes frames
- Reader reads frames successfully
- Verify: System works without metadata

## 5. Edge Cases

**Feature Files**: `EdgeCases.feature`, `Performance.feature`

### 5.1 Exact Buffer Fit
- Setup buffer "test-exact-fit" (payload: 10240 bytes)
- Writer writes frame of exactly 10240 - 16 bytes (accounting for header)
- Verify: Frame written successfully, buffer is completely full

### 5.2 Minimum Frame Size
- Writer attempts to write 0-byte frame
- Verify: Write rejected (zero-sized frames not allowed)
- Writer writes 1-byte frame
- Verify: Success with proper header (16 bytes overhead)

### 5.3 Wrap-Around Behavior
- Setup buffer "test-wrap" (payload: 10KB)
- Writer writes 6KB frame
- Reader reads frame
- Writer writes 7KB frame
- Verify: Writer waits for reader to free start of buffer
- Reader signals space available
- Verify: 7KB frame written at buffer start

### 5.5 Wrap-Around With Wasted Space
- Setup buffer (payload: 10KB)
- Writer writes frame that leaves 100 bytes at end
- Writer attempts to write 200-byte frame
- Verify: Writer writes wrap marker at current position
- Verify: payload_free_bytes reduced by wasted space
- Verify: Frame written at buffer start
- Verify: Reader detects wrap marker and jumps to start

### 5.6 Continuous Free Space Calculation
- Setup buffer with specific read/write positions
- Test continuous_free_bytes calculation when:
  - write_pos > read_pos (normal case)
  - write_pos < read_pos (wrapped case)
  - write_pos == read_pos (empty or full)
  - read_pos == 0 (cannot wrap)
- Verify calculation matches specification

### 5.4 Maximum Frame Size
- Setup buffer with large payload size (100MB)
- Writer attempts to write frame matching exactly the payload size minus header
- Verify: Frame written successfully
- Writer attempts to write frame exceeding payload size
- Verify: Write rejected with appropriate error

## 6. Synchronization Tests

**Feature File**: `Synchronization.feature`

### 6.1 Rapid Write-Read Cycles
- Setup buffer "test-rapid" with small size (10KB)
- Writer continuously writes 1KB frames
- Reader continuously reads and immediately signals
- Run for 10000 iterations
- Verify: No deadlocks, correct sequence numbers throughout

### 6.2 Burst Write
- Setup buffer "test-burst" (100KB)
- Writer writes 50 frames (1KB each) as fast as possible
- Reader starts reading after 1 second delay
- Verify: All frames received correctly

### 6.3 Slow Reader
- Setup buffer "test-slow-reader" (50KB)
- Writer writes frames continuously
- Reader reads one frame every 100ms
- Verify: Writer blocks appropriately when buffer full
- Verify: No frames lost

### 6.4 Semaphore Signal Ordering
- Writer writes frame and signals sem-w
- Before reader wakes, writer writes another frame
- Verify: Reader processes both frames correctly
- Verify: Semaphore count reflects pending frames

## 7. Data Integrity Tests

**Feature Files**: `Synchronization.feature`, `ErrorHandling.feature`

### 7.1 Pattern Validation
- Writer writes frames with known pattern (incrementing bytes)
- Reader validates each byte matches expected pattern
- Test with various frame sizes: 1B, 1KB, 1MB, 10MB
- Verify: No data corruption

### 7.2 Sequence Number Gap Detection
- Writer writes frames 1, 2, 3
- Simulate corruption: directly modify shared memory to change next frame's sequence to 5
- Reader attempts to read frame
- Verify: Reader throws SequenceError exception (expected 4, got 5)
- Verify: Reader cannot proceed without intervention

### 7.3 Memory Barrier Verification
- Writer writes complex structure with multiple fields
- Ensure all fields visible to reader after semaphore signal
- Test on multi-core system under load
- Verify: No partially visible writes

## 8. Platform-Specific Tests

**Feature File**: `PlatformSpecific.feature`

### 8.1 Linux-Specific
- Verify shm_open creates entries in /dev/shm/
- Verify sem_open creates proper named semaphores
- Test with various buffer names including special characters
- Verify proper cleanup in /dev/shm/ after graceful shutdown

### 8.2 Windows-Specific
- Verify CreateFileMapping with proper naming
- Test Global\ vs Local\ namespace for different user contexts
- Verify semaphore limits (max count)
- Test across different user sessions

### 8.3 Cross-Platform Name Compatibility
- Create buffer with name containing only alphanumeric and dash
- Verify same name rules work on both Linux and Windows
- Test maximum name length on both platforms

## 9. Performance Tests

**Feature Files**: `Benchmarks.feature`, `StressTests.feature`

### 9.1 Throughput Measurement
- Setup large buffer (100MB)
- Measure frames/second for various frame sizes
- Measure total bandwidth achieved
- Compare with theoretical shared memory bandwidth

### 9.2 Latency Measurement
- Measure time from writer signal to reader wake
- Measure round-trip time for write-read-signal cycle
- Test with high-frequency small frames
- Verify sub-millisecond latencies

### 9.3 CPU Usage
- Monitor CPU usage during idle (blocked on semaphore)
- Verify near-zero CPU when waiting
- Monitor CPU during active transfer
- Verify efficient data copying

## 10. Error Handling Tests

**Feature Files**: `ErrorHandling.feature`, `StressTests.feature`, `Initialization.feature`

### 10.1 Partial Initialization Failures
- Reader creates shared memory successfully
- Simulate failure creating sem-w semaphore
- Verify: Reader cleans up shared memory before throwing exception
- Verify: No resources leaked

### 10.2 Corrupted OIEB Detection
- Setup buffer successfully
- Corrupt OIEB by setting operation_size to wrong value
- New writer attempts to connect
- Verify: Writer detects invalid OIEB and throws exception
- Corrupt OIEB by setting impossible values (write_pos > buffer_size)
- Verify: Reader/Writer detect corruption

### 10.3 Invalid Frame Header
- Writer writes valid frame
- Directly corrupt frame header in shared memory (set payload_size to 0)
- Reader attempts to read
- Verify: Reader throws InvalidFrameSizeException
- Corrupt frame header with payload_size > remaining buffer
- Verify: Reader detects and handles gracefully

### 10.4 Reader Death During Write
- Setup small buffer (10KB)
- Writer starts writing large frame (8KB)
- While write is in progress, kill reader process
- Verify: Writer detects reader death on next operation
- Verify: Writer throws ReaderDeadException

### 10.5 Writer Death During Read
- Reader waiting on semaphore for data
- Kill writer process while reader is blocked
- Verify: Reader detects writer death after timeout
- Verify: Reader throws WriterDeadException

### 10.6 System Resource Exhaustion
- Create maximum allowed shared memory segments
- Attempt to create one more buffer
- Verify: Appropriate system error is thrown
- Create maximum allowed semaphores
- Attempt to create buffer
- Verify: Appropriate error handling and cleanup

### 10.7 Permission Errors
- Create buffer as user1
- Attempt to connect as user2 without permissions
- Verify: Permission denied error is properly handled
- Verify: No resource corruption

## 11. Edge Case Tests

**Feature File**: `EdgeCases.feature`

### 11.1 Zero-Sized Metadata Block
- Create buffer with BufferConfig(0, 10240)
- Writer attempts to write metadata
- Verify: Metadata write fails appropriately
- Writer proceeds to write frames without metadata
- Verify: System works correctly

### 11.2 Minimum Buffer Sizes
- Create buffer with minimum viable size (sizeof(FrameHeader) + 1)
- Write single-byte frame
- Verify: Works correctly
- Attempt to write 2-byte frame
- Verify: Writer blocks waiting for space

### 11.3 Alternating Frame Sizes
- Write large frame (90% of buffer)
- Write small frame (1 byte)
- Write large frame again
- Verify: Proper wrap-around handling
- Verify: No deadlocks

### 11.4 Semaphore Signal Coalescing
- Writer writes 10 frames rapidly without reader consuming
- Verify: Semaphore count represents pending frames
- Reader wakes and processes all frames
- Verify: All frames read correctly despite coalesced signals

### 11.5 Reader Slower Than Writer
- Small buffer (10KB)
- Writer writes continuously at high speed
- Reader processes with 10ms delay per frame
- Run for 1000 frames
- Verify: No frames lost
- Verify: Writer blocks appropriately
- Verify: Flow control works correctly

## 12. Stress Tests

**Feature File**: `StressTests.feature`

### 12.1 Long Duration
- Run continuous write-read for 24 hours
- Verify no resource leaks
- Verify sequence numbers handle overflow correctly
- Monitor system resource usage

### 12.2 Buffer Exhaustion
- Create maximum number of buffers system allows
- Verify graceful failure when limit reached
- Cleanup and verify resources properly released

### 12.3 Rapid Create/Destroy
- Reader creates and destroys buffer 1000 times
- Verify no resource leaks
- Verify lock files properly cleaned up
- Test with writer attempting to connect during transitions

## 13. Protocol Compliance Tests

**Feature Files**: `EdgeCases.feature`, `PlatformSpecific.feature`

### 13.1 OIEB Consistency
- After each write operation, verify:
  - payload_written_count increments by 1
  - payload_free_bytes decreases by frame size
  - payload_write_pos advances correctly
  - All values are 64-byte aligned
- After each read operation, verify:
  - payload_read_count increments by 1
  - payload_free_bytes increases by frame size
  - payload_read_pos advances correctly

### 13.2 Memory Alignment Verification
- Verify OIEB starts at 64-byte aligned address
- Verify metadata block starts at 64-byte aligned offset
- Verify payload block starts at 64-byte aligned offset
- Write various sized frames
- Verify all data access respects alignment

### 13.3 Lock File Semantics
- Create buffer, verify lock file exists at /tmp/zerobuffer/{name}.lock
- Verify lock file is actually locked (flock/fcntl)
- Kill reader process
- Verify new reader can acquire lock after detecting stale lock
- Verify lock file is removed on graceful shutdown

### 13.4 Semaphore Naming Convention
- Create buffer with various names containing:
  - UUID format (per use case)
  - Special characters that are filesystem-safe
  - Maximum length names
- Verify semaphores created as sem-w-{name} and sem-r-{name}
- Verify both Linux and Windows naming rules

## 14. Duplex Channel Tests

**Feature Files**: `DuplexChannel.feature`, `DuplexAdvanced.feature`

### 14.1 Basic Request-Response
- Client creates duplex channel "duplex-basic"
- Server creates duplex channel with BufferConfig(4KB, 1MB)
- Server starts with echo handler (returns exact request data)
- Client sends 5 requests with different sizes (1B, 1KB, 100KB)
- Verify: Each response matches request data and sequence number
- Verify: Responses can arrive out of order

### 14.2 Sequence Number Correlation
- Setup duplex channel "duplex-sequence"
- Client sends 10 requests rapidly without waiting for responses
- Server responds with 500ms delay, in reverse order
- Client receives all responses
- Verify: Client correctly matches responses to requests using sequence numbers
- Verify: No responses are lost or mismatched

### 14.3 Concurrent Client Operations
- Setup duplex channel "duplex-concurrent"
- Client spawns 5 threads
- Each thread sends 20 requests and waits for its responses
- Server processes requests with variable delays (0-100ms)
- Verify: Each thread receives exactly its 20 responses
- Verify: No cross-thread response delivery

### 14.4 Server Processing Mode - SingleThread
- Setup duplex channel with ProcessingMode.SingleThread
- Client sends 3 requests that each take 1 second to process
- Measure total time
- Verify: Total time ≥ 3 seconds (sequential processing)
- Verify: Responses arrive in order

### 14.5 Mutable vs Immutable Server
- Setup two channels: "duplex-mutable" and "duplex-immutable"
- Both servers implement XOR with key 0xFF
- Send identical 10KB frames to both
- Verify: Both produce identical results
- Verify: Mutable server modifies in-place (no allocations)
- Verify: Immutable server returns new data

### 14.6 Server Death During Processing
- Setup duplex channel "duplex-crash"
- Client sends large request (1MB)
- Server starts processing then crashes after 100ms
- Client detects server death via timeout
- Verify: Client throws appropriate exception
- Verify: Client can detect server PID is gone

### 14.7 Client Death During Response Wait
- Setup duplex channel "duplex-client-crash"
- Server has 2-second processing delay
- Client sends request then dies after 1 second
- Server completes processing and attempts to send response
- Verify: Server detects client death when writing response
- Verify: Server continues to process other requests

### 14.8 Buffer Full on Response Channel
- Setup duplex channel with small buffers (10KB each)
- Server responds with 2x size of request
- Client sends 4KB request
- Server tries to respond with 8KB
- Client doesn't read responses (blocked)
- Verify: Server blocks on response write
- Client reads one response
- Verify: Server unblocks and completes write

### 14.9 Zero-Copy Client Operations  
- Setup duplex channel "duplex-zerocopy"
- Client uses AcquireRequestBuffer() for zero-copy write
- Write test pattern directly to buffer
- Call CommitRequest()
- Server echoes request
- Verify: Pattern intact in response
- Verify: No memory allocations in send path

### 14.10 Channel Cleanup on Dispose
- Create duplex channel "duplex-cleanup"
- Send 5 requests, receive 3 responses
- Dispose server while 2 responses pending
- Verify: Server stops gracefully
- Verify: Client receives exception on pending responses
- Verify: All shared memory cleaned up
- Verify: New server can reuse same channel name

## 15. Benchmark Tests

**Feature File**: `Benchmarks.feature`

### 15.1 Latency Benchmark
- Setup: Single reader/writer, various frame sizes (1KB, 64KB, 1MB, 10MB)
- Measure: Time from write_frame() start to read_frame() complete
- Operations: 10,000 iterations per frame size
- Report: Min, Max, Mean, P50, P90, P99, P99.9 latencies
- Expected: Sub-millisecond for small frames

### 15.2 Throughput Benchmark
- Setup: Continuous write/read, no delays
- Frame sizes: 1KB, 64KB, 1MB, 10MB
- Duration: 60 seconds per size
- Report: Frames/sec, MB/sec, CPU usage %
- Expected: Saturate memory bandwidth

### 15.3 Wrap-Around Overhead Benchmark
- Setup: Buffer size = 1.5x frame size (forces frequent wrap)
- Measure: Performance with and without wrap-around
- Report: % overhead from wrap-around
- Expected: <5% performance impact

### 15.4 Memory Barrier Cost Benchmark
- Measure: Time for atomic fence operations
- Compare: With and without memory barriers
- Verify: Data integrity maintained
- Expected: <100ns per barrier

### 15.5 Semaphore Signaling Overhead
- Measure: sem_post/sem_wait operation cost
- Test rates: 1Hz, 100Hz, 1kHz, 10kHz, 100kHz
- Report: CPU usage and latency impact
- Expected: Negligible until >10kHz

### 15.6 Buffer Utilization Under Load
- Setup: Writer faster than reader
- Monitor: Buffer utilization % over time
- Verify: Degradation detection triggers at 80%
- Report: Time to degradation, recovery time

## Test Execution Notes

- Each test should verify both success and failure paths
- All tests should clean up resources on completion
- Tests should be runnable in parallel with unique buffer names
- Memory corruption tests should use separate processes for safety
- Platform-specific tests should be conditionally executed
- All timeouts should be configurable for different system speeds
- Benchmark tests should run on isolated CPU cores for consistency

## Mode-Specific Considerations

### Same Process Mode (Mode 1)
- Process lifecycle tests (2.1, 2.2, 2.3) should simulate crashes using exceptions or thread termination
- PID checks will always show process as alive - tests should use alternative mechanisms
- Resource cleanup tests may need special handling since the process doesn't actually die
- Memory barriers are critical for correctness in this mode

### Separate Process Mode (Mode 2)
- Process lifecycle tests can use actual process termination (kill, abort)
- PID checks work as specified in the test scenarios
- Resource cleanup can be tested more realistically
- Fork() on Unix or CreateProcess() on Windows should be used
- Parent process should manage child process lifecycle

### Test Framework Requirements
- Test harness should provide a --mode flag or similar to select execution mode
- Each test should be written to work correctly in both modes
- Mode-specific behavior should be abstracted into helper functions
- Test results should clearly indicate which mode was used