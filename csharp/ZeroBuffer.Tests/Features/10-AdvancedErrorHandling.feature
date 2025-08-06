Feature: Advanced Error Handling Tests
    Tests for complex error scenarios and resource management failures

    Background:
        Given the test environment is initialized
        And all processes are ready

    Scenario: Test 10.1 - Partial Initialization Failures
        Given the test creates shared memory successfully for 'test-partial'

        When simulate failure creating sem-w semaphore

        Then the 'reader' process should clean up shared memory
        And the 'reader' process should throw appropriate exception
        And the test should verify no resources leaked

    Scenario: Test 10.2 - Invalid Frame Header Variations
        Given the 'reader' process creates buffer 'test-frame-corruption' with default config

        When the 'writer' process connects to buffer 'test-frame-corruption'
        And the 'writer' process writes valid frame

        Then test multiple corruption scenarios:
        | corruption_type | expected_result |
        | payload_size > remaining | reader handles gracefully |
        | sequence_number invalid | reader detects error |
        | header magic corrupted | reader rejects frame |

    Scenario: Test 10.3 - Reader Death During Active Write
        Given the 'reader' process creates buffer 'test-reader-death-write' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-reader-death-write'
        And the 'writer' process starts writing large frame '8192' bytes

        And the 'reader' process is killed while write in progress

        And the 'writer' process detects reader death on next operation

        Then the 'writer' process should throw reader dead exception

    Scenario: Test 10.4 - Writer Death During Read
        Given the 'reader' process creates buffer 'test-writer-death-read' with default config

        When the 'writer' process connects to buffer 'test-writer-death-read'

        And the 'reader' process waits on semaphore for data

        And the 'writer' process crashes while reader blocked

        Then the 'reader' process should detect writer death after timeout '5' seconds
        And the 'reader' process should throw writer dead exception

    Scenario: Test 10.5 - System Resource Exhaustion
        Given create maximum allowed semaphores

        When attempt to create buffer 'test-sem-exhausted'

        Then the creation should fail with system error
        And appropriate error handling should occur
        And partial resources should be cleaned up

    Scenario: Test 10.6 - Permission Errors
        Given the reader is 'user1'
        And the 'reader' process creates buffer 'test-permissions' with restrictive permissions

        When the writer is 'user2'
        And another writer attempts to connect without permissions

        Then permission denied error should be handled
        And no resource corruption should occur