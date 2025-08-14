Feature: Duplex Channel Tests
    Tests for bidirectional communication using duplex channels

    Background:

    Scenario: Test 13.1 - Basic Request-Response
        Given the 'server' process creates immutable duplex channel 'duplex-basic' with metadata size '4096' and payload size '1048576'
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-basic'
        And the 'client' process sends request with size '1'

        Then response should match request with size '1'

        When the 'client' process sends request with size '1024'
        Then response should match request with size '1024'

        When the 'client' process sends request with size '102400'
        Then response should match request with size '102400'

        # Note: v1.0.0 - sequence numbers are internal only, not in response payload

    Scenario: Test 13.2 - Request-Response Order Preservation
        # v1.0.0: Sequence correlation is handled internally by Reader/Writer
        Given the 'server' process creates immutable duplex channel 'duplex-sequence' with default config
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-sequence'
        And the 'client' process sends '10' requests sequentially

        Then the 'client' process receives '10' responses in order
        And responses should match requests by content
        And no responses should be lost or duplicated

    Scenario: Test 13.3 - Concurrent Client Operations
        Given the 'server' process creates immutable duplex channel 'duplex-concurrent' with default config
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-concurrent'
        And the 'client' process sends '20' requests from single thread

        Then the 'client' process receives exactly '20' responses
        And all '20' responses match their requests
        # Note: v1.0.0 - Multi-threaded client access requires external synchronization

    Scenario: Test 13.4 - Server Processing Mode SingleThread
        Given the 'server' process creates immutable duplex channel 'duplex-singlethread' with processing mode 'SingleThread'
        And the 'server' process starts handler with '100' ms processing time

        When the 'client' process creates duplex channel client 'duplex-singlethread'
        And the 'client' process sends '3' requests sequentially
        And the 'client' process measures total response time

        Then total time should be at least '300' ms
        And responses should arrive in order

    Scenario: Test 13.5 - Server Death During Processing
        Given the 'server' process creates immutable duplex channel 'duplex-crash' with default config
        And the 'server' process starts handler that crashes after '100' ms

        When the 'client' process creates duplex channel client 'duplex-crash'
        And the 'client' process sends request of '1024' bytes

        Then the 'server' process simulates crash during processing

        When the 'client' process waits for response with timeout '5' seconds

        Then the 'client' process should detect server death
        And an appropriate exception should be thrown

    Scenario: Test 13.6 - Buffer Full on Response Channel
        Given the 'server' process creates immutable duplex channel 'duplex-full' with metadata size '1024' and payload size '10240'
        And the 'server' process starts handler that doubles request size

        When the 'client' process creates duplex channel client 'duplex-full'
        And the 'client' process sends request with size '4096'
        And the 'client' process does not read responses

        Then the 'server' process attempts to send response of '8192' bytes
        And the 'server' process should block on response write

        When the 'client' process reads one response

        Then the 'server' process should unblock and complete write

    Scenario: Test 13.7 - Zero-Copy Client Operations
        Given the 'server' process creates immutable duplex channel 'duplex-zerocopy' with default config
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-zerocopy'
        And the 'client' process acquires zero-copy request buffer of size '4096'
        And the 'client' process fills buffer with test pattern
        And the 'client' process commits request

        Then response should contain same test pattern
        And no memory allocations in send path

    Scenario: Test 13.8 - Immutable Server Handler Types
        # v1.0.0: Only immutable server is supported, mutable will be in v2.0.0
        Given the 'server' process creates immutable duplex channel 'duplex-transform' with default config
        And the 'server' process starts handler that implements XOR with key '0xFF'

        When the 'client' process creates duplex channel client 'duplex-transform'
        And the 'client' process sends '10240' byte frame with test pattern

        Then response should contain XOR transformed data
        And the server handler receives immutable request frame
        And the server handler returns new response data

    Scenario: Test 13.9 - Client Death During Response Wait
        Given the 'server' process creates immutable duplex channel 'duplex-client-crash' with default config
        And the 'server' process starts handler with '2' second processing delay

        When the 'client' process creates duplex channel client 'duplex-client-crash'
        And the 'client' process sends request
        And the 'client' process simulates crash after '1' second

        Then the 'server' process completes processing after '2' seconds
        And the 'server' process attempts to send response
        And the 'server' process detects client death when writing
        And the 'server' process continues processing other requests

    Scenario: Test 13.10 - Channel Cleanup on Dispose
        Given the 'server' process creates immutable duplex channel 'duplex-cleanup' with default config
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-cleanup'
        And the 'client' process sends '5' requests
        And the 'client' process receives '3' responses

        Then the 'server' process disposes server while '2' responses pending

        When the 'client' process attempts to receive pending responses

        Then the 'client' process should receive exception on pending
        And all shared memory should be cleaned up

        When a new 'server' process creates immutable duplex channel 'duplex-cleanup'

        Then the new server should reuse same channel name successfully
