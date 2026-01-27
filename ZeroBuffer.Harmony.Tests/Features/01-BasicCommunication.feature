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

        Then the 'reader' process should read frame with sequence '1';
        And the 'reader' process signals space available
        And the 'reader' process should read frame with sequence '2';
        And the 'reader' process signals space available
        And the 'reader' process should read frame with sequence '3';
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

        Then the 'reader' process should read 4 frames with sizes '100,1024,10240,1' in order

    Scenario: Test 1.6 - Slow Reader With Fast Writer
        # This test verifies that frames are not overwritten when reader is slow
        # Buffer is sized to hold ~5 frames, writer sends 20 frames as fast as possible
        # Reader adds 100ms delay between reads to simulate slow processing
        # All 20 frames must be received in correct sequence order
        Given the 'reader' process creates buffer 'test-slow-reader' with metadata size '64' and payload size '10240'

        When the 'writer' process connects to buffer 'test-slow-reader'
        And the 'writer' process writes '20' frames of size '1024' as fast as possible

        And the 'reader' process reads frames with '100' ms delay between each read

        Then the 'reader' process should have read '20' frames
        And the 'reader' process should verify all frames have sequential sequence numbers starting from '1'
        And no sequence errors should have occurred

