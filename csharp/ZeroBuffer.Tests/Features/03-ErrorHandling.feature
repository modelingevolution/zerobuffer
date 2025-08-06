Feature: Error Handling and Recovery Tests
    Tests for error conditions, corruption detection, and recovery scenarios

    Background:

    Scenario: Test 3.1 - Metadata Write-Once Enforcement
        Given the 'reader' process creates buffer 'test-metadata-once' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-metadata-once'
        And the 'writer' process writes metadata with size '500'
        And the 'writer' process writes frame with data 'test'
        And the 'writer' process attempts to write metadata again with size '200'

        Then the 'writer' process verifies the second metadata write should fail
        And the 'reader' process verifies the original metadata should remain unchanged

    Scenario: Test 3.2 - Metadata Size Overflow
        Given the 'reader' process creates buffer 'test-metadata-overflow' with metadata size '1024' and payload size '10240'

        When the 'writer' process connects to buffer 'test-metadata-overflow'
        And the 'writer' process attempts to write metadata with size '2048'

        Then the 'writer' process verifies the metadata write should fail with size error




    Scenario: Test 3.3 - Zero Metadata Configuration
        Given the 'reader' process creates buffer 'test-no-metadata' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-no-metadata'
        And the 'writer' process writes frames without metadata

        Then the 'reader' process should read frames successfully
        And the 'reader' process verifies the system should work without metadata
