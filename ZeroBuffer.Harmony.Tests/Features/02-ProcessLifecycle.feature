Feature: Process Lifecycle Tests
    Tests for process crash detection and recovery scenarios

    Background:
        Given we run in harmony

    Scenario: Test 2.1 - Writer Crash Detection
        Given the 'reader' process creates buffer 'test-writer-crash' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-writer-crash'
        And the 'writer' process writes frame with data 'alive'

        Then the 'reader' process should read frame with data 'alive'

        When the 'writer' process is killed

        Then wait for '2' seconds
        And the 'reader' process should timeout or detect writer death on next read

    Scenario: Test 2.2 - Reader Crash Detection
        Given the 'reader' process creates buffer 'test-reader-crash' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-reader-crash'
        And the 'writer' process writes frame with sequence '1'

        Then the 'reader' process should read frame with sequence '1'

        When the 'writer' process fills buffer completely
        And the 'reader' process crashes

        Then wait for '2' seconds
        And the 'writer' process should detect reader death on next write

    Scenario: Test 2.3 - Reader Replacement After Crash
        Given the 'reader' process creates buffer 'test-reader-replace' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-reader-replace'
        And the 'writer' process writes frame with sequence '1'

        Then the 'reader' process should read frame with sequence '1'

        When the 'reader' process crashes

        And a new 'reader' process attempts to create buffer 'test-reader-replace' with metadata size '1024' and payload size '10240'

        Then the new reader should detect stale resources
        And the new reader should clean up stale resources
        And the new reader should create fresh buffer successfully

        When the 'writer' process detects reader death and reconnects to buffer 'test-reader-replace'
        And the 'writer' process writes frame with sequence '1'

        Then the 'reader' process should read frame with sequence '1'

    Scenario: Test 2.4 - Multiple Writer Rejection
        Given the 'reader' process creates buffer 'test-multi-writer' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-multi-writer'

        When a second 'writer' process attempts to connect to buffer 'test-multi-writer'
        Then the connection should fail with writer exists error


