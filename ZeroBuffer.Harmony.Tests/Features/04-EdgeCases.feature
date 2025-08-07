Feature: Edge Cases and Boundary Conditions
    Tests for edge cases, minimum/maximum values, and boundary conditions
    
    Background:
        
    Scenario: Test 4.1 - Alternating Frame Sizes
        Given the 'reader' process creates buffer 'test-alternating' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-alternating'
        And the 'writer' process writes large frame using '90%' of buffer
        And the 'writer' process writes small frame of '1' byte
        And the 'writer' process attempts to write large frame again

        Then the 'writer' process proper wrap-around handling should occur
        And the 'writer' process no deadlocks should happen

    Scenario: Test 4.2 - Semaphore Signal Coalescing
        Given the 'reader' process creates buffer 'test-coalesce' with default config

        When the 'writer' process connects to buffer 'test-coalesce'
        And the 'writer' process writes '10' frames rapidly without reader consuming

        Then the 'writer' process the semaphore count should represent pending frames

        When the 'reader' process wakes and processes all frames

        Then the 'reader' process all frames should be read correctly
        And the 'reader' process the coalesced signals should be handled properly

    Scenario: Test 4.3 - Zero-Sized Metadata Block
        Given the 'reader' process creates buffer 'test-zero-metadata' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-zero-metadata'
        And the 'writer' process attempts to write metadata

        Then the 'writer' process metadata write should fail appropriately

        When the 'writer' process writes frame without metadata

        Then the 'writer' process should verify frame write succeeded
        And the 'reader' process should verify system works correctly without metadata
        
    Scenario: Test 4.4 - Minimum Buffer Sizes
        Given the 'reader' process creates buffer 'test-minimum' with minimum viable size '64'

        When the 'writer' process connects to buffer 'test-minimum'
        And the 'writer' process writes single byte frame

        Then the 'writer' process should verify write succeeded

        When the 'writer' process attempts to write '49' byte frame

        Then the 'writer' process should receive FrameTooLargeException
        
        
    Scenario: Test 4.5 - Reader Slower Than Writer
        Given the 'reader' process creates buffer 'test-reader-slower' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-reader-slower'
        And writes continuously at high speed

        When the 'reader' process reads with '10' ms delay per frame
        And the test runs for '1000' frames

        Then the 'reader' process should receive all frames without loss
        
        Then the 'writer' process should block appropriately
        And flow control should work correctly

    Scenario: Test 4.6 - Exact Buffer Fit
        Given the 'reader' process creates buffer 'test-exact-fit' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-exact-fit'
        And the 'writer' process writes frame with exact size '10224'

        Then buffer should be completely full
        And no more writes should be possible

        When the 'reader' process reads frame
        Then the frame should have size '10224'
        
        When the 'reader' process signals space available
        Then the 'writer' process should be able to write again

    Scenario: Test 4.7 - Minimum Frame Size
        Given the 'reader' process creates buffer 'test-min-frame' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-min-frame'
        And the 'writer' process attempts to write frame with size '0'

        Then the 'writer' process should fail with zero size error

        When the 'writer' process writes frame with size '1'

        Then the 'reader' process should read frame with size '1'
        And frame overhead should be '16' bytes

    Scenario: Test 4.8 - Wrap-Around Behavior
        Given the 'reader' process creates buffer 'test-wrap' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-wrap'
        And the 'writer' process writes frame with size '6144'

        Then the 'reader' process should read frame with size '6144'
        When the 'reader' process signals space available
        And the 'writer' process writes frame with size '7168'

        Then the 'writer' process should wait for space

        When the 'reader' process signals space available

        Then the 'writer' process should complete write at buffer start

    Scenario: Test 4.9 - Rapid Write-Read Cycles
        Given the 'reader' process creates buffer 'test-rapid' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-rapid'
        And the 'writer' process writes '10000' frames of size '1024' rapidly
        And the 'reader' process reads all frames and signals immediately

        Then all sequence numbers should be correct
        And no frames should be lost
        And no deadlocks should occur

    Scenario: Test 4.10 - Buffer Full With Multiple Writers Rejected
        Given the 'reader' process creates buffer 'test-multi-writer-full' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-multi-writer-full'
        And the 'writer' process fills buffer to '80%' capacity
        And the 'writer' process a second writer process attempts to connect to buffer 'test-multi-writer-full'
        
        Then the 'writer' process the second writer should fail with writer exists error

        When the 'writer' process continues filling buffer to '100%'

        Then the 'writer' process should block waiting for space

    Scenario: Test 4.11 - Concurrent Initialization Race
        Given two 'reader' processes

        When both simultaneously attempt to create buffer 'race-test'

        Then only one should succeed
        And other should receive appropriate error
        And no resource corruption should occur

    Scenario: Test 4.12 - Writer Before Reader
        Given the system is ready

        When a 'writer' process attempts to connect to non-existent buffer 'no-reader'

        Then the connection should fail with appropriate error
        And error should indicate no shared memory found