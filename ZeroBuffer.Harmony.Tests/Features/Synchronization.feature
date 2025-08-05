Feature: Synchronization and Data Integrity Tests
    Tests for concurrent operations, synchronization, and data integrity

    Background:
        Given the test mode is configured

    Scenario: Test 6.2 - Burst Write Performance
        Given the 'reader' process creates buffer 'test-burst' with metadata size '0' and payload size '102400'

        When the 'writer' process connects to buffer 'test-burst'
        And the 'writer' process writes '50' frames of size '1024' as fast as possible

        And the 'reader' process waits '1' second before reading
        And the 'reader' process reads all '50' frames

        Then all frames should be received correctly
        And data integrity should be maintained

    Scenario: Test 6.3 - Slow Reader Flow Control
        Given the 'reader' process creates buffer 'test-slow-reader' with metadata size '0' and payload size '51200'

        When the 'writer' process connects to buffer 'test-slow-reader'
        And the 'writer' process writes frames continuously

        And the 'reader' process reads one frame every '100' ms

        Then the 'writer' process should block when buffer full
        And no frames should be lost
        And flow control should work correctly

    Scenario: Test 6.4 - Semaphore Signal Ordering
        Given the 'reader' process creates buffer 'test-semaphore' with default config

        When the 'writer' process connects to buffer 'test-semaphore'
        And the 'writer' process writes frame and signal
        And the 'writer' process immediately writes another frame before reader wakes

        Then the 'reader' process should process both frames correctly
        And the semaphore count should reflect pending frames

    Scenario: Test 7.3 - Memory Barrier Verification
        Given the 'reader' process creates buffer 'test-memory-barrier' with default config

        When the 'writer' process connects to buffer 'test-memory-barrier'
        And the 'writer' process writes complex structure with multiple fields
        And the system ensures memory barriers are in place

        Then the 'reader' process should read structure after semaphore signal
        And all fields should be fully visible
        And no partially visible writes should occur

    Scenario: Test 7.4 - Pattern Validation
        Given the 'reader' process creates buffer 'test-pattern' with default config

        When the 'writer' process connects to buffer 'test-pattern'
        And the 'writer' process writes frame with size '1' using incrementing pattern
        And the 'writer' process writes frame with size '1024' using incrementing pattern
        And the 'writer' process writes frame with size '1048576' using incrementing pattern
        And the 'writer' process writes frame with size '10485760' using incrementing pattern

        Then the 'reader' process should validate each byte matches expected pattern
        And no data corruption should be detected

    Scenario: Test 11.1 - Alternating Frame Sizes
        Given the 'reader' process creates buffer 'test-alternating' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-alternating'
        And the 'writer' process writes large frame using '90%' of buffer
        And the 'writer' process writes small frame of '1' byte
        And the 'writer' process attempts to write large frame again

        Then proper wrap-around handling should occur
        And no deadlocks should happen

    Scenario: Test 11.2 - Semaphore Signal Coalescing
        Given the 'reader' process creates buffer 'test-coalesce' with default config

        When the 'writer' process connects to buffer 'test-coalesce'
        And the 'writer' process writes '10' frames rapidly without reader consuming

        Then the semaphore count should represent pending frames

        When the 'reader' process wakes and processes all frames

        Then all frames should be read correctly
        And the coalesced signals should be handled properly

    Scenario: Test 12.1 - Rapid Create Destroy Cycles
        Given perform '1000' iterations of:

        When the 'reader' process creates buffer 'test-rapid-cycle' with default config
        And the 'reader' process destroys buffer

        Then no resource leaks should occur
        And lock files should be properly cleaned
        And system should remain stable
