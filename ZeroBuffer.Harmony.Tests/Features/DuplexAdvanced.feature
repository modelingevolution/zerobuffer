Feature: Advanced Duplex Channel Tests
    Advanced tests for duplex channel mutable/immutable modes and cleanup

    Background:
        Given the test mode is configured

    Scenario: Test 14.5 - Mutable vs Immutable Server
        Given the 'server' process creates duplex channel 'duplex-mutable' with mutable handler
        And the 'server' process creates duplex channel 'duplex-immutable' with immutable handler
        And both handlers implement XOR with key '0xFF'

        When the 'client' process creates duplex channel client 'duplex-mutable'
        And the 'client' process creates duplex channel client 'duplex-immutable'
        And the 'client' process sends identical '10240' byte frames to both

        Then both should produce identical XOR results
        And the mutable server should modify in-place
        And the mutable server should have no allocations
        And the immutable server should return new data

    Scenario: Test 14.7 - Client Death During Response Wait
        Given the 'server' process creates duplex channel 'duplex-client-crash' with default config
        And the 'server' process starts handler with '2' second processing delay

        When the 'client' process creates duplex channel client 'duplex-client-crash'
        And the 'client' process sends request
        And the 'client' process simulates crash after '1' second

        Then the 'server' process completes processing after '2' seconds
        And the 'server' process attempts to send response
        And the 'server' process detects client death when writing
        And the 'server' process continues processing other requests

    Scenario: Test 14.10 - Channel Cleanup on Dispose
        Given the 'server' process creates duplex channel 'duplex-cleanup' with default config
        And the 'server' process starts echo handler

        When the 'client' process creates duplex channel client 'duplex-cleanup'
        And the 'client' process sends '5' requests
        And the 'client' process receives '3' responses

        Then the 'server' process disposes server while '2' responses pending

        When the 'client' process attempts to receive pending responses

        Then the 'client' process should receive exception on pending
        And all shared memory should be cleaned up

        When a new 'server' process creates duplex channel 'duplex-cleanup'

        Then the new server should reuse same channel name successfully
