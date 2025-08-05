Feature: Initialization and Resource Management Tests
    Tests for buffer initialization, resource cleanup, and lifecycle management

    Background:
        Given the test mode is configured

    Scenario: Test 3.2 - Concurrent Initialization Race
        Given two 'reader' processes

        When both simultaneously attempt to create buffer 'race-test'

        Then only one should succeed
        And other should receive appropriate error
        And no resource corruption should occur

    Scenario: Test 3.3 - Writer Before Reader
        Given the system is ready

        When a 'writer' process attempts to connect to non-existent buffer 'no-reader'

        Then the connection should fail with appropriate error
        And error should indicate no shared memory found

    Scenario: Test 4.3 - Zero Metadata Configuration
        Given the 'reader' process creates buffer 'test-no-metadata' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-no-metadata'
        And the 'writer' process writes frames without metadata

        Then the 'reader' process should read frames successfully
        And the system should work without metadata

    Scenario: Test 10.5 - Writer Death During Read
        Given the 'reader' process creates buffer 'test-writer-death-read' with default config

        When the 'writer' process connects to buffer 'test-writer-death-read'

        And the 'reader' process waits on semaphore for data

        And the 'writer' process crashes while reader blocked

        Then the 'reader' process should detect writer death after timeout '5' seconds
        And the 'reader' process should throw writer dead exception

    Scenario: Test 10.6 - System Resource Exhaustion
        Given create maximum allowed semaphores

        When attempt to create buffer 'test-sem-exhausted'

        Then the creation should fail with system error
        And appropriate error handling should occur
        And partial resources should be cleaned up
