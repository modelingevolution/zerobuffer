@platform-specific
Feature: Platform-Specific Tests
    Tests for platform-specific behavior and resource management

    Background:

    @linux
    Scenario: Test 8.1 - Linux Shared Memory Verification
        Given the platform is 'Linux'

        And the 'reader' process creates buffer 'test-linux-shm' with default config

        Then verify shared memory entry exists in '/dev/shm/'
        And verify proper named semaphores created
        And test with buffer names containing special characters

        When gracefully shutdown

        Then verify proper cleanup in '/dev/shm/'

    @windows
    Scenario: Test 8.2 - Windows Named Objects Verification
        Given the platform is 'Windows'

        And the 'reader' process creates buffer 'test-windows-objects' with default config

        Then verify CreateFileMapping with proper naming
        And test Global vs Local namespace for different contexts
        And verify semaphore limits and max count

        When test across different user sessions

        Then verify proper isolation or sharing as expected

    Scenario: Test 8.3 - Cross-Platform Name Compatibility
        And the 'reader' process creates buffer 'test-name-compat-123' with default config

        Then verify name contains only alphanumeric and dash
        And same name rules work on both platforms

        When test maximum name length

        Then both platforms should handle it correctly

    Scenario: Test 8.4 - Stale Resource Cleanup
        Given manually create stale lock file for 'stale-test'
        And create orphaned shared memory for 'stale-test'
        And create orphaned semaphores for 'stale-test'

        When the 'reader' process attempts to create buffer 'stale-test'

        Then the stale resources should be detected
        And the old resources should be cleaned up
        And the new buffer should be created successfully
