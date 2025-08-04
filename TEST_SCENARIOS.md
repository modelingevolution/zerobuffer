# ZeroBuffer Protocol Acceptance Test Scenarios

## 1. Basic Communication Tests

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

## 14. Benchmark Tests

### 14.1 Latency Benchmark
- Setup: Single reader/writer, various frame sizes (1KB, 64KB, 1MB, 10MB)
- Measure: Time from write_frame() start to read_frame() complete
- Operations: 10,000 iterations per frame size
- Report: Min, Max, Mean, P50, P90, P99, P99.9 latencies
- Expected: Sub-millisecond for small frames

### 14.2 Throughput Benchmark
- Setup: Continuous write/read, no delays
- Frame sizes: 1KB, 64KB, 1MB, 10MB
- Duration: 60 seconds per size
- Report: Frames/sec, MB/sec, CPU usage %
- Expected: Saturate memory bandwidth

### 14.3 Wrap-Around Overhead Benchmark
- Setup: Buffer size = 1.5x frame size (forces frequent wrap)
- Measure: Performance with and without wrap-around
- Report: % overhead from wrap-around
- Expected: <5% performance impact

### 14.4 Memory Barrier Cost Benchmark
- Measure: Time for atomic fence operations
- Compare: With and without memory barriers
- Verify: Data integrity maintained
- Expected: <100ns per barrier

### 14.5 Semaphore Signaling Overhead
- Measure: sem_post/sem_wait operation cost
- Test rates: 1Hz, 100Hz, 1kHz, 10kHz, 100kHz
- Report: CPU usage and latency impact
- Expected: Negligible until >10kHz

### 14.6 Buffer Utilization Under Load
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