Feature: Duplex Channel Tests
    Tests for bidirectional communication using duplex channels

    Background:
        Given the test mode is configured

    Scenario: Test 14.1 - Basic Request-Response
        Given the 'server' process creates duplex channel 'duplex-basic' with metadata size '4096' and payload size '1048576'
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-basic'
        And the 'client' process sends request with size '1'

        Then response should match request with size '1'

        When the 'client' process sends request with size '1024'
        Then response should match request with size '1024'

        When the 'client' process sends request with size '102400'
        Then response should match request with size '102400'

        And all responses should have correct sequence numbers

    Scenario: Test 14.2 - Sequence Number Correlation
        Given the 'server' process creates duplex channel 'duplex-sequence' with default config
        And the 'server' process starts delayed echo handler with '500' ms delay

        When the 'client' process creates duplex channel client 'duplex-sequence'
        And the 'client' process sends '10' requests rapidly without waiting

        Then the 'server' process responds in reverse order

        When the 'client' process receives all '10' responses

        Then responses should match requests by sequence number
        And no responses should be lost or mismatched

    Scenario: Test 14.3 - Concurrent Client Operations
        Given the 'server' process creates duplex channel 'duplex-concurrent' with default config
        And the 'server' process starts variable delay handler '0-100' ms

        When the 'client' process creates duplex channel client 'duplex-concurrent'
        And the 'client' process spawns '5' threads
        And each thread sends '20' requests

        Then each thread receives exactly '20' responses
        And no cross-thread response delivery occurs
        And all '100' total responses are accounted for

    Scenario: Test 14.4 - Server Processing Mode SingleThread
        Given the 'server' process creates duplex channel 'duplex-singlethread' with processing mode 'SingleThread'
        And the 'server' process starts handler with '1' second processing time

        When the 'client' process creates duplex channel client 'duplex-singlethread'
        And the 'client' process sends '3' requests simultaneously
        And the 'client' process measures total response time

        Then total time should be at least '3' seconds
        And responses should arrive in order

    Scenario: Test 14.5 - Server Death During Processing
        Given the 'server' process creates duplex channel 'duplex-crash' with default config
        And the 'server' process starts handler that crashes after '100' ms

        When the 'client' process creates duplex channel client 'duplex-crash'
        And the 'client' process sends large request of '1048576' bytes

        Then the 'server' process simulates crash during processing

        When the 'client' process waits for response with timeout '5' seconds

        Then the 'client' process should detect server death
        And an appropriate exception should be thrown

    Scenario: Test 14.6 - Buffer Full on Response Channel
        Given the 'server' process creates duplex channel 'duplex-full' with metadata size '1024' and payload size '10240'
        And the 'server' process starts handler that doubles request size

        When the 'client' process creates duplex channel client 'duplex-full'
        And the 'client' process sends request with size '4096'
        And the 'client' process does not read responses

        Then the 'server' process attempts to send response of '8192' bytes
        And the 'server' process should block on response write

        When the 'client' process reads one response

        Then the 'server' process should unblock and complete write

    Scenario: Test 14.7 - Zero-Copy Client Operations
        Given the 'server' process creates duplex channel 'duplex-zerocopy' with default config
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-zerocopy'
        And the 'client' process acquires zero-copy request buffer of size '4096'
        And the 'client' process fills buffer with test pattern
        And the 'client' process commits request

        Then response should contain same test pattern
        And no memory allocations in send path
