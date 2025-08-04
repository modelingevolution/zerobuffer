Feature: Basic Communication Tests
    Tests for fundamental ZeroBuffer communication patterns
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 1.1 - Simple Write-Read Cycle
        Given the reader is 'csharp'
        And create buffer 'test-basic' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-basic'
        And write metadata with size '100'
        And write frame with size '1024' and sequence '1'
        
        Then the reader is 'csharp'
        And read frame should have sequence '1' and size '1024'
        And frame data should be valid
        And signal space available
        
    Scenario: Test 1.2 - Multiple Frames Sequential
        Given the reader is 'csharp'
        And create buffer 'test-multi' with metadata size '1024' and payload size '102400'
        
        When the writer is 'python'
        And connect to buffer 'test-multi'
        And write frame with sequence '1'
        And write frame with sequence '2'
        And write frame with sequence '3'
        
        Then the reader is 'csharp'
        And read frame should have sequence '1'
        And signal space available
        And read frame should have sequence '2'
        And signal space available
        And read frame should have sequence '3'
        And all frames should maintain sequential order
        
    Scenario: Test 1.3 - Buffer Full Handling
        Given the reader is 'csharp'
        And create buffer 'test-full' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-full'
        And write frames until buffer is full
        
        Then the writer is 'python'
        And next write should timeout or return buffer full
        
        When the reader is 'csharp'
        And read one frame
        And signal space available
        
        Then the writer is 'python'
        And write should succeed immediately
        
    Scenario: Test 1.4 - Zero-Copy Write Operations
        Given the reader is 'csharp'
        And create buffer 'test-zerocopy' with metadata size '1024' and payload size '102400'
        
        When the writer is 'cpp'
        And connect to buffer 'test-zerocopy'
        And request zero-copy frame of size '4096'
        And fill zero-copy buffer with test pattern
        And commit zero-copy frame
        
        Then the reader is 'csharp'
        And read frame should have size '4096'
        And frame data should match test pattern
        
    Scenario: Test 1.5 - Mixed Frame Sizes
        Given the reader is 'csharp'
        And create buffer 'test-mixed' with metadata size '1024' and payload size '102400'
        
        When the writer is 'python'
        And connect to buffer 'test-mixed'
        And write frame with size '100'
        And write frame with size '1024'
        And write frame with size '10240'
        And write frame with size '1'
        
        Then the reader is 'csharp'
        And read 4 frames with correct sizes in order
        
    Scenario: Test 1.6 - Metadata Update During Operation
        Given the reader is 'csharp'
        And create buffer 'test-metadata-update' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-metadata-update'
        And write metadata 'version=1.0'
        And write frame with data 'frame1'
        
        Then the reader is 'csharp'
        And metadata should be 'version=1.0'
        And read frame should return 'frame1'
        
        When the writer is 'python'
        And write metadata 'version=2.0'
        And write frame with data 'frame2'
        
        Then the reader is 'csharp'
        And metadata should be 'version=2.0'
        And read frame should return 'frame2'