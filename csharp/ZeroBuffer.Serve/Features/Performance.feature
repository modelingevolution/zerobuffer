Feature: Performance and Edge Case Tests
    Tests for buffer performance, edge cases, and boundary conditions
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 3.1 - Exact Buffer Fit
        Given the reader is 'csharp'
        And create buffer 'test-exact-fit' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-exact-fit'
        And write frame with exact size '10224'
        
        Then buffer should be completely full
        And no more writes should be possible
        
        When the reader is 'csharp'
        And read frame should have size '10224'
        And signal space available
        
        Then the writer is 'python'
        And write should succeed again
        
    Scenario: Test 3.2 - Minimum Frame Size
        Given the reader is 'csharp'
        And create buffer 'test-min-frame' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-min-frame'
        And attempt to write frame with size '0'
        
        Then write should fail with zero size error
        
        When write frame with size '1'
        
        Then the reader is 'csharp'
        And read frame should have size '1'
        And frame overhead should be '16' bytes
        
    Scenario: Test 3.3 - Wrap-Around Behavior
        Given the reader is 'csharp'
        And create buffer 'test-wrap' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-wrap'
        And write frame with size '6144'
        
        Then the reader is 'csharp'
        And read frame should have size '6144'
        And signal space available
        
        When the writer is 'python'
        And write frame with size '7168'
        
        Then write should wait for space
        
        When the reader is 'csharp'
        And signal space available
        
        Then the writer is 'python'
        And write should complete at buffer start
        
    Scenario: Test 3.4 - Rapid Write-Read Cycles
        Given the reader is 'csharp'
        And create buffer 'test-rapid' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-rapid'
        And write '10000' frames of size '1024' rapidly
        
        And the reader is 'csharp'
        And read all frames and signal immediately
        
        Then all sequence numbers should be correct
        And no frames should be lost
        And no deadlocks should occur
        
    Scenario: Test 3.5 - Buffer Full With Multiple Writers Rejected
        Given the reader is 'csharp'
        And create buffer 'test-multi-writer-full' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-multi-writer-full'
        And fill buffer to '80%' capacity
        
        Then the writer is 'cpp'
        And attempt to connect to buffer 'test-multi-writer-full'
        And connection should fail with writer exists error
        
        When the writer is 'python'
        And continue filling buffer to '100%'
        
        Then next write should block waiting for space