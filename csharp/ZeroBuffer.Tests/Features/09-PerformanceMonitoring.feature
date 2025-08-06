Feature: Performance Monitoring Tests
    Tests for CPU usage and performance monitoring

    Background:
        Given the test environment is initialized
        And all processes are ready

    Scenario: Test 9.1 - CPU Usage Monitoring
        Given the 'reader' process creates buffer 'test-cpu-usage' with default config

        When the 'writer' process connects to buffer 'test-cpu-usage'

        Then monitor CPU usage during idle (blocked on semaphore)
        And verify near-zero CPU when waiting

        When transfer data actively

        Then monitor CPU during active transfer
        And verify efficient data copying