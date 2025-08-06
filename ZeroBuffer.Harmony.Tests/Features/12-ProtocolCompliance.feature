Feature: Protocol Compliance Tests
    WARNING! Tests for low-level protocol verification and system conventions, these tests are in DEBUG mode. the reader and writer in RELEASE would not verify this. 

    Background:
        Given the test environment is initialized
        And all processes are ready
	    And we are in DEBUG mode for protocol compliance tests

    Scenario: Test 12.1 - Protocol Compliance OIEB
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
        
    Scenario: Test 12.2 - Memory Alignment Verification
        Given the 'reader' process creates buffer 'test-alignment' with default config

        Then the 'reader' process should verify OIEB starts at 64-byte aligned address
        And should verify metadata block starts at 64-byte aligned offset
        And should verify payload block starts at 64-byte aligned offset

        When the 'writer' process connects to buffer 'test-alignment'
        And writes various sized frames

        Then the 'writer' process should verify all data access respects alignment

    Scenario: Test 12.3 - Lock File Semantics
        And the 'reader' process creates buffer 'test-lock-semantics' with default config

        Then verify lock file exists at correct location
        And verify lock file is actually locked

        When kill reader process

        Then new reader can acquire lock after detecting stale

        When graceful shutdown

        Then verify lock file is removed

    Scenario: Test 12.4 - Semaphore Naming Convention
        Given create buffers with various names:
        | buffer_name | description |
        | 550e8400-e29b-41d4-a716-446655440000 | UUID format |
        | test_buffer-123 | alphanumeric with special chars |
        | very_long_buffer_name_that_tests_maximum_length_limits | max length |

        Then verify semaphores created as 'sem-w-{name}' and 'sem-r-{name}'
        And verify both Linux and Windows naming rules respected