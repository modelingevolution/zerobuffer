@stress-test
@long-running
Feature: Stress and Endurance Tests
    Long-running stress tests for stability and resource leak detection

    Background:
        Given the test mode is configured
        And stress test environment is prepared

    Scenario: Test 9.3 - CPU Usage Monitoring
        Given the 'reader' process creates buffer 'test-cpu-usage' with default config

        When the 'writer' process connects to buffer 'test-cpu-usage'

        Then monitor CPU usage during idle (blocked on semaphore)
        And verify near-zero CPU when waiting

        When transfer data actively

        Then monitor CPU during active transfer
        And verify efficient data copying

    Scenario: Test 10.1 - Partial Initialization Failures
        Given the test creates shared memory successfully for 'test-partial'

        When simulate failure creating sem-w semaphore

        Then the 'reader' process should clean up shared memory
        And the 'reader' process should throw appropriate exception
        And the test should verify no resources leaked

    Scenario: Test 10.3 - Invalid Frame Header Variations
        Given the 'reader' process creates buffer 'test-frame-corruption' with default config

        When the 'writer' process connects to buffer 'test-frame-corruption'
        And the 'writer' process writes valid frame

        Then test multiple corruption scenarios:
        | corruption_type | expected_result |
        | payload_size > remaining | reader handles gracefully |
        | sequence_number invalid | reader detects error |
        | header magic corrupted | reader rejects frame |

    Scenario: Test 10.4 - Reader Death During Active Write
        Given the 'reader' process creates buffer 'test-reader-death-write' with metadata size '0' and payload size '10240'

        When the 'writer' process connects to buffer 'test-reader-death-write'
        And the 'writer' process starts writing large frame '8192' bytes

        And the 'reader' process is killed while write in progress

        And the 'writer' process detects reader death on next operation

        Then the 'writer' process should throw reader dead exception

    Scenario: Test 12.1 - Long Duration Stress Test
        Given the 'reader' process creates buffer 'test-long-duration' with default config

        When the 'writer' process connects to buffer 'test-long-duration'
        And the test runs continuous write-read for '24' hours

        Then verify no resource leaks
        And verify sequence numbers handle overflow
        And monitor system resource usage
        And ensure stable operation throughout

    Scenario: Test 12.2 - Buffer Exhaustion Test
        Given create maximum number of buffers system allows

        When reach system limit

        Then the test should verify graceful failure when limit reached

        When cleanup all buffers

        Then the test should verify resources properly released
        And the system should return to normal state

    Scenario: Test 12.3 - Rapid Create Destroy Under Load
        Given the test spawns writer attempting connections continuously

        When the test creates and destroys buffer '1000' times

        Then the test should verify no resource leaks
        And the test should verify lock files cleaned up
        And the system should handle writer connection attempts gracefully
