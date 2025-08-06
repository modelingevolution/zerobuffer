Feature: Basic Communication Tests
    Tests for fundamental ZeroBuffer communication patterns

    Background:
        Given the test environment is initialized
        And all processes are ready

    Scenario: Test 1.1 - Simple Write-Read Cycle
        Given the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-basic'
        And the 'writer' process writes metadata with size '100'
        And the 'writer' process writes frame with size '1024' and sequence '1'

        Then the 'reader' process should read frame with sequence '1' and size '1024'
        And the 'reader' process should validate frame data
        And the 'reader' process signals space available

    Scenario: Test 1.2 - Multiple Frames Sequential
        Given the 'reader' process creates buffer 'test-multi' with metadata size '1024' and payload size '102400'

        When the 'writer' process connects to buffer 'test-multi'
        And the 'writer' process writes frame with sequence '1'
        And the 'writer' process writes frame with sequence '2'
        And the 'writer' process writes frame with sequence '3'

        Then the 'reader' process should read frame with sequence '1'
        And the 'reader' process signals space available
        And the 'reader' process should read frame with sequence '2'
        And the 'reader' process signals space available
        And the 'reader' process should read frame with sequence '3'
        And the 'reader' process should verify all frames maintain sequential order

    Scenario: Test 1.3 - Buffer Full Handling
        Given the 'reader' process creates buffer 'test-full' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-full'
        And the 'writer' process writes frames until buffer is full

        Then the 'writer' process should experience timeout on next write

        When the 'reader' process reads one frame
        And the 'reader' process signals space available

        Then the 'writer' process should write successfully immediately

    Scenario: Test 1.4 - Zero-Copy Write Operations
        Given the 'reader' process creates buffer 'test-zerocopy' with metadata size '1024' and payload size '102400'

        When the 'writer' process connects to buffer 'test-zerocopy'
        And the 'writer' process requests zero-copy frame of size '4096'
        And the 'writer' process fills zero-copy buffer with test pattern
        And the 'writer' process commits zero-copy frame

        Then the 'reader' process should read frame with size '4096'
        And the 'reader' process should verify frame data matches test pattern

    Scenario: Test 1.5 - Mixed Frame Sizes
        Given the 'reader' process creates buffer 'test-mixed' with metadata size '1024' and payload size '102400'

        When the 'writer' process connects to buffer 'test-mixed'
        And the 'writer' process writes frame with size '100'
        And the 'writer' process writes frame with size '1024'
        And the 'writer' process writes frame with size '10240'
        And the 'writer' process writes frame with size '1'

        Then the 'reader' process should read 4 frames with correct sizes in order

    Scenario: Test 1.6 - Metadata Update During Operation
        Given the 'reader' process creates buffer 'test-metadata-update' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-metadata-update'
        And the 'writer' process writes metadata 'version=1.0'
        And the 'writer' process writes frame with data 'frame1'

        Then the 'reader' process should have metadata 'version=1.0'
        And the 'reader' process should read frame with data 'frame1'

        When the 'writer' process writes metadata 'version=2.0'
        And the 'writer' process writes frame with data 'frame2'

        Then the 'reader' process should have metadata 'version=2.0'
        And the 'reader' process should read frame with data 'frame2'
