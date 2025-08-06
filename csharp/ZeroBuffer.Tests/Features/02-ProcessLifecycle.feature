Feature: Process Lifecycle Tests
    Tests for process crash detection and recovery scenarios

    Background:

    Scenario: Test 2.1 - Writer Crash Detection
        Given the 'reader' process creates buffer 'test-writer-crash' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-writer-crash'
        And the 'writer' process writes frame with data 'alive'

        Then the 'reader' process should read frame with data 'alive'
        And the writer should be connected

        When the 'writer' process crashes

        Then wait for '2' seconds
        And the writer should be disconnected
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

        And a new 'reader' process connects to existing buffer 'test-reader-replace'

        And the 'writer' process writes frame with sequence '2'

        Then the 'reader' process should read frame with sequence '2'
        And the buffer should continue functioning normally

    Scenario: Test 2.4 - Multiple Writer Rejection
        Given the 'reader' process creates buffer 'test-multi-writer' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-multi-writer'

        When a second 'writer' process attempts to connect to buffer 'test-multi-writer'
        Then the connection should fail with writer exists error

    Scenario: Test 2.5 - Clean Shutdown Sequence
        Given the 'reader' process creates buffer 'test-shutdown' with metadata size '1024' and payload size '10240'

        And the 'writer' process connects to buffer 'test-shutdown'
        And the 'writer' process writes frame with data 'final message'
        When the 'writer' process closes connection gracefully

        Then the 'reader' process should read frame with data 'final message'
        And the writer should be disconnected
        And the 'reader' process cleanup should succeed
