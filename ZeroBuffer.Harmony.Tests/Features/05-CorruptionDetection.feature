Feature: Corruption Detection Tests
    Tests for detecting data corruption and invalid buffer states

    Background:
        Given the test environment is initialized
        And all processes are ready

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
        
    Scenario: Test 5.5 - Wrap-Around With Wasted Space
        Given the 'reader' process creates buffer 'test-waste' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-waste'
        And writes frame that leaves '100' bytes at end with 'incremental' pattern
        And attempts to write '200' byte frame with 'sequential' pattern

        Then the 'writer' process should write wrap marker at current position
        And should see payload_free_bytes reduced by wasted space
        And frame should be written at buffer start

        When the 'reader' process reads next frame

        Then the 'reader' process should detect wrap marker
        And should jump to buffer start
        And should read '200' byte frame successfully with 'sequential' pattern
        
    Scenario: Test 5.6 - Continuous Free Space Calculation
        Given the 'reader' process creates buffer 'test-free-space' with specific configuration

        When the system tests continuous_free_bytes calculation with:
        | write_pos | read_pos | expected_result | scenario |
        | 5000 | 2000 | calculated | write_pos > read_pos |
        | 2000 | 5000 | calculated | write_pos < read_pos |
        | 5000 | 5000 | calculated | write_pos == read_pos empty |
        | 0 | 0 | calculated | both at start |
        | 5000 | 0 | calculated | read_pos at start cannot wrap |
        
        Then the 'reader' process calculations should match specification
        
    Scenario: Test 5.7 - Maximum Frame Size
        Given the 'reader' process creates buffer 'test-max-frame' with metadata size '0' and payload size '104857600'

        When the 'writer' process connects to buffer 'test-max-frame'
        And writes frame matching exactly payload size minus header

        Then the 'writer' process should verify frame was written successfully

        When the 'writer' process attempts to write frame exceeding payload size

        Then the 'writer' process write should be rejected with appropriate error