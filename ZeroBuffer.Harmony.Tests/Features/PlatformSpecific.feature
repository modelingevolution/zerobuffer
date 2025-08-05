@platform-specific
Feature: Platform-Specific Tests
    Tests for platform-specific behavior and resource management
    
    Background:
        Given the test mode is configured
        
    @linux
    Scenario: Test 8.1 - Linux Shared Memory Verification
        Given the platform is 'Linux'
        And the reader is 'csharp'
        And create buffer 'test-linux-shm' with default config
        
        Then verify shared memory entry exists in '/dev/shm/'
        And verify proper named semaphores created
        And test with buffer names containing special characters
        
        When gracefully shutdown
        
        Then verify proper cleanup in '/dev/shm/'
        
    @windows
    Scenario: Test 8.2 - Windows Named Objects Verification
        Given the platform is 'Windows'
        And the reader is 'csharp'
        And create buffer 'test-windows-objects' with default config
        
        Then verify CreateFileMapping with proper naming
        And test Global vs Local namespace for different contexts
        And verify semaphore limits and max count
        
        When test across different user sessions
        
        Then verify proper isolation or sharing as expected
        
    Scenario: Test 8.3 - Cross-Platform Name Compatibility
        Given the reader is 'csharp'
        And create buffer 'test-name-compat-123' with default config
        
        Then verify name contains only alphanumeric and dash
        And same name rules work on both platforms
        
        When test maximum name length
        
        Then both platforms should handle it correctly
        
    Scenario: Test 13.3 - Lock File Semantics
        Given the reader is 'csharp'
        And create buffer 'test-lock-semantics' with default config
        
        Then verify lock file exists at correct location
        And verify lock file is actually locked
        
        When kill reader process
        
        Then new reader can acquire lock after detecting stale
        
        When graceful shutdown
        
        Then verify lock file is removed
        
    Scenario: Test 13.4 - Semaphore Naming Convention
        Given create buffers with various names:
        | buffer_name | description |
        | 550e8400-e29b-41d4-a716-446655440000 | UUID format |
        | test_buffer-123 | alphanumeric with special chars |
        | very_long_buffer_name_that_tests_maximum_length_limits | max length |
        
        Then verify semaphores created as 'sem-w-{name}' and 'sem-r-{name}'
        And verify both Linux and Windows naming rules respected
        
    @permission-test
    Scenario: Test 10.7 - Permission Errors
        Given the reader is 'user1'
        And create buffer 'test-permissions' with restrictive permissions
        
        When the writer is 'user2'
        And attempt to connect without permissions
        
        Then permission denied error should be handled
        And no resource corruption should occur