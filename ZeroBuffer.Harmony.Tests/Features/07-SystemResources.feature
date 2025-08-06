Feature: System Resources Tests
    Tests for resource management and memory barriers

    Background:
        Given the test environment is initialized
        And all processes are ready

    Scenario: Test 7.1 - Partial Initialization Failure
        Given attempt to create buffer 'test-partial-init' with default config
        And the test simulates semaphore creation failure

        Then the buffer creation should fail
        And the shared memory should be cleaned up
        And no resources should be leaked

    Scenario: Test 7.2 - System Resource Exhaustion
        Given create maximum allowed shared memory segments

        When attempt to create one more buffer 'test-exhausted'

        Then the creation should fail with system error
        And an appropriate error message should be returned

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