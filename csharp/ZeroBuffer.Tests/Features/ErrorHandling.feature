Feature: Error Handling and Recovery Tests
    Tests for error conditions, corruption detection, and recovery scenarios

    Background:
        Given the test mode is configured

    Scenario: Test 4.1 - Metadata Write-Once Enforcement
        Given the 'reader' process creates buffer 'test-metadata-once' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-metadata-once'
        And the 'writer' process writes metadata with size '500'
        And the 'writer' process writes frame with data 'test'
        And the 'writer' process attempts to write metadata again with size '200'

        Then the second metadata write should fail
        And the original metadata should remain unchanged

    Scenario: Test 4.2 - Metadata Size Overflow
        Given the 'reader' process creates buffer 'test-metadata-overflow' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-metadata-overflow'
        And the 'writer' process attempts to write metadata with size '2048'

        Then the metadata write should fail with size error

    Scenario: Test 5.1 - Corrupted OIEB Detection
        Given the 'reader' process creates buffer 'test-corrupt-oieb' with default config

        When the 'writer' process connects to buffer 'test-corrupt-oieb'
        And the 'writer' process writes frame with data 'valid'

        And the test corrupts OIEB field 'operation_size' to wrong value

        When a second 'writer' process attempts to connect to buffer 'test-corrupt-oieb'

        Then the connection should fail with invalid OIEB error

    Scenario: Test 5.2 - Invalid Frame Header Detection
        Given the 'reader' process creates buffer 'test-invalid-header' with default config

        When the 'writer' process connects to buffer 'test-invalid-header'
        And the 'writer' process writes frame with data 'test'

        And the test corrupts frame header 'payload_size' to '0'

        When the 'reader' process attempts to read frame

        Then the read should fail with invalid frame size error

    Scenario: Test 5.3 - Reader Death During Write
        Given the 'reader' process creates buffer 'test-reader-death' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-reader-death'
        And the 'writer' process starts writing large frame of '8192' bytes

        And the 'reader' process crashes while write in progress

        And the 'writer' process completes write operation
        And the 'writer' process attempts next operation

        Then the 'writer' process should detect reader death
        And the 'writer' process should throw reader dead exception

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

    Scenario: Test 8.1 - Stale Resource Cleanup
        Given manually create stale lock file for 'stale-test'
        And create orphaned shared memory for 'stale-test'
        And create orphaned semaphores for 'stale-test'

        When the 'reader' process attempts to create buffer 'stale-test'

        Then the stale resources should be detected
        And the old resources should be cleaned up
        And the new buffer should be created successfully
