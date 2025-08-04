@benchmark
Feature: Performance Benchmark Tests
    Performance benchmarks for latency, throughput, and overhead measurements
    
    Background:
        Given the test mode is configured
        And benchmark environment is prepared
        
    Scenario: Test 15.1 - Latency Benchmark
        Given the reader is 'csharp'
        And create buffer 'bench-latency' with default config
        
        When the writer is 'python'
        And connect to buffer 'bench-latency'
        
        Then measure latency for frame sizes:
        | size | iterations | expected |
        | 1KB | 10000 | sub-millisecond |
        | 64KB | 10000 | sub-millisecond |
        | 1MB | 10000 | low milliseconds |
        | 10MB | 10000 | low milliseconds |
        
        And report Min, Max, Mean, P50, P90, P99, P99.9
        
    Scenario: Test 15.2 - Throughput Benchmark
        Given the reader is 'csharp'
        And create buffer 'bench-throughput' with large config
        
        When the writer is 'python'
        And connect to buffer 'bench-throughput'
        
        Then measure throughput for '60' seconds with frame sizes:
        | size | metric |
        | 1KB | frames/sec, MB/sec |
        | 64KB | frames/sec, MB/sec |
        | 1MB | frames/sec, MB/sec |
        | 10MB | frames/sec, MB/sec |
        
        And report CPU usage percentage
        And expect to saturate memory bandwidth
        
    Scenario: Test 15.3 - Wrap-Around Overhead Benchmark
        Given buffer size is '1.5x' frame size to force wrap
        
        When measure performance with wrap-around
        And measure performance without wrap-around
        
        Then calculate overhead percentage
        And expect less than '5%' performance impact
        
    Scenario: Test 15.4 - Memory Barrier Cost Benchmark
        Given measure atomic fence operations
        
        When compare with and without memory barriers
        
        Then verify data integrity maintained
        And expect less than '100' ns per barrier
        
    Scenario: Test 15.5 - Semaphore Signaling Overhead
        Given measure sem_post/sem_wait operation cost
        
        When test at rates:
        | rate | expected_impact |
        | 1Hz | negligible |
        | 100Hz | negligible |
        | 1kHz | negligible |
        | 10kHz | negligible |
        | 100kHz | measurable |
        
        Then report CPU usage and latency impact
        
    Scenario: Test 15.6 - Buffer Utilization Under Load
        Given the reader is 'csharp'
        And create buffer 'bench-utilization' with default config
        And writer is faster than reader
        
        When monitor buffer utilization over time
        
        Then verify degradation detection at '80%'
        And report time to degradation
        And report recovery time