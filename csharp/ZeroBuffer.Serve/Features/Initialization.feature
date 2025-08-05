Feature: Initialization and Resource Management Tests
    Tests for buffer initialization, resource cleanup, and lifecycle management
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 3.2 - Concurrent Initialization Race
        Given two readers 'csharp' and 'python'
        
        When both simultaneously attempt to create buffer 'race-test'
        
        Then only one should succeed
        And other should receive appropriate error
        And no resource corruption should occur
        
    Scenario: Test 3.3 - Writer Before Reader
        When the writer is 'python'
        And attempt to connect to non-existent buffer 'no-reader'
        
        Then connection should fail with appropriate error
        And error should indicate no shared memory found
        
    Scenario: Test 4.3 - Zero Metadata Configuration
        Given the reader is 'csharp'
        And create buffer 'test-no-metadata' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-no-metadata'
        And write frames without metadata
        
        Then the reader is 'csharp'
        And read frames successfully
        And system should work without metadata
        
    Scenario: Test 10.5 - Writer Death During Read
        Given the reader is 'csharp'
        And create buffer 'test-writer-death-read' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-writer-death-read'
        
        Then the reader is 'csharp'
        And wait on semaphore for data
        
        When the writer is 'python'
        And simulate crash while reader blocked
        
        Then the reader is 'csharp'
        And detect writer death after timeout '5' seconds
        And throw writer dead exception
        
    Scenario: Test 10.6 - System Resource Exhaustion
        Given create maximum allowed semaphores
        
        When attempt to create buffer 'test-sem-exhausted'
        
        Then creation should fail with system error
        And appropriate error handling should occur
        And partial resources should be cleaned up