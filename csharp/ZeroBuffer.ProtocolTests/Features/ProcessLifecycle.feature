Feature: Process Lifecycle Tests
    Tests for process crash detection and recovery scenarios
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 2.1 - Writer Crash Detection
        Given the reader is 'csharp'
        And create buffer 'test-writer-crash' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-writer-crash'
        And write frame with data 'alive'
        
        Then the reader is 'csharp'
        And read frame should return 'alive'
        And writer should be connected
        
        When the writer is 'python'
        And simulate crash
        
        Then the reader is 'csharp'
        And wait for '2' seconds
        And writer should be disconnected
        And next read should timeout or indicate writer death
        
    Scenario: Test 2.2 - Reader Crash Detection
        Given the reader is 'cpp'
        And create buffer 'test-reader-crash' with metadata size '1024' and payload size '10240'
        
        When the writer is 'csharp'
        And connect to buffer 'test-reader-crash'
        And write frame with sequence '1'
        
        Then the reader is 'cpp'
        And read frame should have sequence '1'
        
        When the writer is 'csharp'
        And fill buffer completely
        
        And the reader is 'cpp'
        And simulate crash
        
        Then the writer is 'csharp'
        And wait for '2' seconds
        And next write should detect reader death
        
    Scenario: Test 2.3 - Reader Replacement After Crash
        Given the reader is 'python'
        And create buffer 'test-reader-replace' with metadata size '1024' and payload size '10240'
        
        When the writer is 'csharp'
        And connect to buffer 'test-reader-replace'
        And write frame with sequence '1'
        
        Then the reader is 'python'
        And read frame should have sequence '1'
        
        When the reader is 'python'
        And simulate crash
        
        And the reader is 'cpp'
        And attempt to create buffer 'test-reader-replace' with metadata size '1024' and payload size '10240'
        
        Then detect stale resources
        And clean up stale resources
        And create fresh buffer successfully
        
        When the writer is 'csharp'
        And detect reader death and reconnect to buffer 'test-reader-replace'
        And write frame with sequence '1'
        
        Then the reader is 'cpp'
        And read frame should have sequence '1'
        
    Scenario: Test 2.4 - Multiple Writer Rejection
        Given the reader is 'csharp'
        And create buffer 'test-multi-writer' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-multi-writer'
        
        Then the writer is 'cpp'
        And attempt to connect to buffer 'test-multi-writer'
        And connection should fail with writer exists error
        
