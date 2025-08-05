@stress-test
@long-running
Feature: Stress and Endurance Tests
    Long-running stress tests for stability and resource leak detection
    
    Background:
        Given the test mode is configured
        And stress test environment is prepared
        
    Scenario: Test 9.3 - CPU Usage Monitoring
        Given the reader is 'csharp'
        And create buffer 'test-cpu-usage' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-cpu-usage'
        
        Then monitor CPU usage during idle (blocked on semaphore)
        And verify near-zero CPU when waiting
        
        When transfer data actively
        
        Then monitor CPU during active transfer
        And verify efficient data copying
        
    Scenario: Test 10.1 - Partial Initialization Failures
        Given the reader is 'csharp'
        And create shared memory successfully for 'test-partial'
        
        When simulate failure creating sem-w semaphore
        
        Then reader should clean up shared memory
        And throw appropriate exception
        And verify no resources leaked
        
    Scenario: Test 10.3 - Invalid Frame Header Variations
        Given the reader is 'csharp'
        And create buffer 'test-frame-corruption' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-frame-corruption'
        And write valid frame
        
        Then test multiple corruption scenarios:
        | corruption_type | expected_result |
        | payload_size > remaining | reader handles gracefully |
        | sequence_number invalid | reader detects error |
        | header magic corrupted | reader rejects frame |
        
    Scenario: Test 10.4 - Reader Death During Active Write
        Given the reader is 'csharp'
        And create buffer 'test-reader-death-write' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-reader-death-write'
        And start writing large frame '8192' bytes
        
        Then the reader is 'csharp'
        And kill reader process while write in progress
        
        When the writer is 'python'
        And detect reader death on next operation
        
        Then throw reader dead exception
        
    Scenario: Test 12.1 - Long Duration Stress Test
        Given the reader is 'csharp'
        And create buffer 'test-long-duration' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-long-duration'
        And run continuous write-read for '24' hours
        
        Then verify no resource leaks
        And verify sequence numbers handle overflow
        And monitor system resource usage
        And ensure stable operation throughout
        
    Scenario: Test 12.2 - Buffer Exhaustion Test
        Given create maximum number of buffers system allows
        
        When reach system limit
        
        Then verify graceful failure when limit reached
        
        When cleanup all buffers
        
        Then verify resources properly released
        And system returns to normal state
        
    Scenario: Test 12.3 - Rapid Create Destroy Under Load
        Given spawn writer attempting connections continuously
        
        When the reader is 'csharp'
        And create and destroy buffer '1000' times
        
        Then verify no resource leaks
        And verify lock files cleaned up
        And handle writer connection attempts gracefully