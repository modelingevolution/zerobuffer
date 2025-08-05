Feature: Synchronization and Data Integrity Tests
    Tests for concurrent operations, synchronization, and data integrity
    
    Background:
        
    Scenario: Test 6.2 - Burst Write Performance
        Given the reader is 'csharp'
        And create buffer 'test-burst' with metadata size '0' and payload size '102400'
        
        When the writer is 'python'
        And connect to buffer 'test-burst'
        And write '50' frames of size '1024' as fast as possible
        
        Then the reader is 'csharp'
        And wait '1' second before reading
        And read all '50' frames
        
        Then all frames should be received correctly
        And data integrity should be maintained
        
    Scenario: Test 6.3 - Slow Reader Flow Control
        Given the reader is 'csharp'
        And create buffer 'test-slow-reader' with metadata size '0' and payload size '51200'
        
        When the writer is 'python'
        And connect to buffer 'test-slow-reader'
        And write frames continuously
        
        And the reader is 'csharp'
        And read one frame every '100' ms
        
        Then writer should block when buffer full
        And no frames should be lost
        And flow control should work correctly
        
    Scenario: Test 6.4 - Semaphore Signal Ordering
        Given the reader is 'csharp'
        And create buffer 'test-semaphore' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-semaphore'
        And write frame and signal
        And immediately write another frame before reader wakes
        
        Then the reader is 'csharp'
        And process both frames correctly
        And semaphore count should reflect pending frames
        
    Scenario: Test 7.3 - Memory Barrier Verification
        Given the reader is 'csharp'
        And create buffer 'test-memory-barrier' with default config
        
        When the writer is 'cpp'
        And connect to buffer 'test-memory-barrier'
        And write complex structure with multiple fields
        And ensure memory barriers are in place
        
        Then the reader is 'csharp'
        And read structure after semaphore signal
        And all fields should be fully visible
        And no partially visible writes should occur
        
    Scenario: Test 7.4 - Pattern Validation
        Given the reader is 'csharp'
        And create buffer 'test-pattern' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-pattern'
        And write frame with size '1' using incrementing pattern
        And write frame with size '1024' using incrementing pattern
        And write frame with size '1048576' using incrementing pattern
        And write frame with size '10485760' using incrementing pattern
        
        Then the reader is 'csharp'
        And validate each byte matches expected pattern
        And no data corruption should be detected
        
    Scenario: Test 11.1 - Alternating Frame Sizes
        Given the reader is 'csharp'
        And create buffer 'test-alternating' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-alternating'
        And write large frame using '90%' of buffer
        And write small frame of '1' byte
        And attempt to write large frame again
        
        Then proper wrap-around handling should occur
        And no deadlocks should happen
        
    Scenario: Test 11.2 - Semaphore Signal Coalescing
        Given the reader is 'csharp'
        And create buffer 'test-coalesce' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-coalesce'
        And write '10' frames rapidly without reader consuming
        
        Then semaphore count should represent pending frames
        
        When the reader is 'csharp'
        And wake and process all frames
        
        Then all frames should be read correctly
        And coalesced signals should be handled properly
        
    Scenario: Test 12.1 - Rapid Create Destroy Cycles
        Given perform '1000' iterations of:
        When the reader is 'csharp'
        And create buffer 'test-rapid-cycle' with default config
        And destroy buffer
        
        Then no resource leaks should occur
        And lock files should be properly cleaned
        And system should remain stable