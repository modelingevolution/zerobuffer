Feature: Duplex Channel Tests
    Tests for bidirectional communication using duplex channels
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 14.1 - Basic Request-Response
        Given the server is 'csharp'
        And create duplex channel 'duplex-basic' with metadata size '4096' and payload size '1048576'
        And start echo handler
        
        When the client is 'python'
        And create duplex channel client 'duplex-basic'
        And send request with size '1'
        
        Then response should match request with size '1'
        
        When send request with size '1024'
        Then response should match request with size '1024'
        
        When send request with size '102400'
        Then response should match request with size '102400'
        
        And all responses should have correct sequence numbers
        
    Scenario: Test 14.2 - Sequence Number Correlation
        Given the server is 'csharp'
        And create duplex channel 'duplex-sequence' with default config
        And start delayed echo handler with '500' ms delay
        
        When the client is 'python'
        And create duplex channel client 'duplex-sequence'
        And send '10' requests rapidly without waiting
        
        Then the server is 'csharp'
        And respond in reverse order
        
        When the client is 'python'
        And receive all '10' responses
        
        Then responses should match requests by sequence number
        And no responses should be lost or mismatched
        
    Scenario: Test 14.3 - Concurrent Client Operations
        Given the server is 'csharp'
        And create duplex channel 'duplex-concurrent' with default config
        And start variable delay handler '0-100' ms
        
        When the client is 'python'
        And create duplex channel client 'duplex-concurrent'
        And spawn '5' threads
        And each thread sends '20' requests
        
        Then each thread receives exactly '20' responses
        And no cross-thread response delivery occurs
        And all '100' total responses are accounted for
        
    Scenario: Test 14.4 - Server Processing Mode SingleThread
        Given the server is 'csharp'
        And create duplex channel 'duplex-singlethread' with processing mode 'SingleThread'
        And start handler with '1' second processing time
        
        When the client is 'python'
        And create duplex channel client 'duplex-singlethread'
        And send '3' requests simultaneously
        And measure total response time
        
        Then total time should be at least '3' seconds
        And responses should arrive in order
        
    Scenario: Test 14.5 - Server Death During Processing
        Given the server is 'csharp'
        And create duplex channel 'duplex-crash' with default config
        And start handler that crashes after '100' ms
        
        When the client is 'python'
        And create duplex channel client 'duplex-crash'
        And send large request of '1048576' bytes
        
        Then the server is 'csharp'
        And simulate crash during processing
        
        When the client is 'python'
        And wait for response with timeout '5' seconds
        
        Then client should detect server death
        And appropriate exception should be thrown
        
    Scenario: Test 14.6 - Buffer Full on Response Channel
        Given the server is 'csharp'
        And create duplex channel 'duplex-full' with metadata size '1024' and payload size '10240'
        And start handler that doubles request size
        
        When the client is 'python'
        And create duplex channel client 'duplex-full'
        And send request with size '4096'
        And do not read responses
        
        Then the server is 'csharp'
        And attempt to send response of '8192' bytes
        And server should block on response write
        
        When the client is 'python'
        And read one response
        
        Then the server is 'csharp'
        And server should unblock and complete write
        
    Scenario: Test 14.7 - Zero-Copy Client Operations
        Given the server is 'csharp'
        And create duplex channel 'duplex-zerocopy' with default config
        And start echo handler
        
        When the client is 'cpp'
        And create duplex channel client 'duplex-zerocopy'
        And acquire zero-copy request buffer of size '4096'
        And fill buffer with test pattern
        And commit request
        
        Then response should contain same test pattern
        And no memory allocations in send path