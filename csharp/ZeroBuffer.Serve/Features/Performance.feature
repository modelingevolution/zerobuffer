Feature: Performance and Edge Case Tests
    Tests for buffer performance, edge cases, and boundary conditions

    Background:
        Given the test mode is configured

    Scenario: Test 3.1 - Exact Buffer Fit
        Given the 'reader' process creates buffer 'test-exact-fit' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-exact-fit'
        And the 'writer' process writes frame with exact size '10224'

        Then buffer should be completely full
        And no more writes should be possible

        When the 'reader' process reads frame
        Then the frame should have size '10224'
        
        When the 'reader' process signals space available
        Then the 'writer' process should be able to write again

    Scenario: Test 3.2 - Minimum Frame Size
        Given the 'reader' process creates buffer 'test-min-frame' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-min-frame'
        And the 'writer' process attempts to write frame with size '0'

        Then the 'writer' process should fail with zero size error

        When the 'writer' process writes frame with size '1'

        Then the 'reader' process should read frame with size '1'
        And frame overhead should be '16' bytes

    Scenario: Test 3.3 - Wrap-Around Behavior
        Given the 'reader' process creates buffer 'test-wrap' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-wrap'
        And the 'writer' process writes frame with size '6144'

        Then the 'reader' process should read frame with size '6144'
        When the 'reader' process signals space available
        And the 'writer' process writes frame with size '7168'

        Then the 'writer' process should wait for space

        When the 'reader' process signals space available

        Then the 'writer' process should complete write at buffer start

    Scenario: Test 3.4 - Rapid Write-Read Cycles
        Given the 'reader' process creates buffer 'test-rapid' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-rapid'
        And the 'writer' process writes '10000' frames of size '1024' rapidly
        And the 'reader' process reads all frames and signals immediately

        Then all sequence numbers should be correct
        And no frames should be lost
        And no deadlocks should occur

    Scenario: Test 3.5 - Buffer Full With Multiple Writers Rejected
        Given the 'reader' process creates buffer 'test-multi-writer-full' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-multi-writer-full'
        And the 'writer' process fills buffer to '80%' capacity
        And a second writer process attempts to connect to buffer 'test-multi-writer-full'
        
        Then the second writer should fail with writer exists error

        When the 'writer' process continues filling buffer to '100%'

        Then the 'writer' process should block waiting for space
