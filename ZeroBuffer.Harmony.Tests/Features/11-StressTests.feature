@stress-test
@long-running
Feature: Stress and Endurance Tests
    Long-running stress tests for stability and resource leak detection

    Background:
        And stress test environment is prepared

    Scenario: Test 11.1 - Rapid Create Destroy Cycles
        Given perform '1000' iterations of:

        When the 'reader' process creates buffer 'test-rapid-cycle' with default config
        And the 'reader' process destroys buffer

        Then no resource leaks should occur
        And lock files should be properly cleaned
        And system should remain stable

    Scenario: Test 11.3 - Long Duration Stress Test
        Given the 'reader' process creates buffer 'test-long-duration' with default config

        When the 'writer' process connects to buffer 'test-long-duration'
        And the test runs continuous write-read for '24' hours

        Then verify no resource leaks
        And verify sequence numbers handle overflow
        And monitor system resource usage
        And ensure stable operation throughout

    Scenario: Test 11.4 - Buffer Exhaustion Test
        Given create maximum number of buffers system allows

        When reach system limit

        Then the test should verify graceful failure when limit reached

        When cleanup all buffers

        Then the test should verify resources properly released
        And the system should return to normal state

    Scenario: Test 11.5 - Rapid Create Destroy Under Load
        Given the test spawns writer attempting connections continuously

        When the test creates and destroys buffer '1000' times

        Then the test should verify no resource leaks
        And the test should verify lock files cleaned up
        And the system should handle writer connection attempts gracefully
