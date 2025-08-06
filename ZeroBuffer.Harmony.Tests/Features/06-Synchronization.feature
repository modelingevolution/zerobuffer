Feature: Synchronization and Data Integrity Tests
    Tests for concurrent operations, synchronization, and data integrity

    Background:

    Scenario: Test 6.1 - Sequence Number Gap Detection
        Given the 'reader' process creates buffer 'test-sequence-gap' with default config

        When the 'writer' process connects to buffer 'test-sequence-gap'
        And the 'writer' process writes frame with sequence '1'
        And the 'writer' process writes frame with sequence '2'
        And the 'writer' process writes frame with sequence '3'

        Then the 'reader' process should read frame with sequence '1'
        And the 'reader' process should read frame with sequence '2'

        When the test corrupts next frame sequence to '5'
        And the 'reader' process attempts to read frame

        Then the read should fail with sequence error
        And the error should show expected '4' got '5'

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

