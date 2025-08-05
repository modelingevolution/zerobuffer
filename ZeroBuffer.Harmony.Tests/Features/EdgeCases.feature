Feature: Edge Cases and Boundary Conditions
    Tests for edge cases, minimum/maximum values, and boundary conditions
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 11.3 - Zero-Sized Metadata Block
        Given the 'reader' process creates buffer 'test-zero-metadata' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-zero-metadata'
        And attempts to write metadata

        Then the 'writer' process metadata write should fail appropriately

        When the 'writer' process writes frame without metadata

        Then the 'writer' process should verify frame write succeeded
        And the 'reader' process should verify system works correctly without metadata
        
    Scenario: Test 11.4 - Minimum Buffer Sizes
        Given the 'reader' process creates buffer 'test-minimum' with minimum viable size '17'

        When the 'writer' process connects to buffer 'test-minimum'
        And writes single byte frame

        Then the 'writer' process should verify write succeeded

        When the 'writer' process attempts to write '2' byte frame

        Then the 'writer' process should block waiting for space
        
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
        
    Scenario: Test 11.5 - Reader Slower Than Writer
        Given the 'reader' process creates buffer 'test-reader-slower' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-reader-slower'
        And writes continuously at high speed

        When the 'reader' process reads with '10' ms delay per frame
        And the test runs for '1000' frames

        Then the 'reader' process should receive all frames without loss
        
        Then the 'writer' process should block appropriately
        And flow control should work correctly
        
    Scenario: Test 13.1 - Protocol Compliance OIEB
        Given the 'reader' process creates buffer 'test-oieb-compliance' with default config

        When the 'writer' process connects to buffer 'test-oieb-compliance'
        And performs multiple write operations

        Then the 'writer' process should verify after each write:
        | field | condition |
        | payload_written_count | increments by 1 |
        | payload_free_bytes | decreases by frame size |
        | payload_write_pos | advances correctly |
        | all values | are 64-byte aligned |
        
        When the 'reader' process performs multiple read operations

        Then the 'reader' process should verify after each read:
        | field | condition |
        | payload_read_count | increments by 1 |
        | payload_free_bytes | increases by frame size |
        | payload_read_pos | advances correctly |
        
    Scenario: Test 13.2 - Memory Alignment Verification
        Given the 'reader' process creates buffer 'test-alignment' with default config

        Then the 'reader' process should verify OIEB starts at 64-byte aligned address
        And should verify metadata block starts at 64-byte aligned offset
        And should verify payload block starts at 64-byte aligned offset

        When the 'writer' process connects to buffer 'test-alignment'
        And writes various sized frames

        Then the 'writer' process should verify all data access respects alignment