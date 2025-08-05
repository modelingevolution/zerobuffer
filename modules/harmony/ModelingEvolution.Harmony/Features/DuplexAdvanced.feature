Feature: Advanced Duplex Channel Tests
    Advanced tests for duplex channel mutable/immutable modes and cleanup
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 14.5 - Mutable vs Immutable Server
        Given the server is 'csharp'
        And create duplex channel 'duplex-mutable' with mutable handler
        And create duplex channel 'duplex-immutable' with immutable handler
        And both implement XOR with key '0xFF'
        
        When the client is 'python'
        And create duplex channel client 'duplex-mutable'
        And create duplex channel client 'duplex-immutable'
        And send identical '10240' byte frames to both
        
        Then both should produce identical XOR results
        And mutable server should modify in-place
        And mutable server should have no allocations
        And immutable server should return new data
        
    Scenario: Test 14.7 - Client Death During Response Wait
        Given the server is 'csharp'
        And create duplex channel 'duplex-client-crash' with default config
        And start handler with '2' second processing delay
        
        When the client is 'python'
        And create duplex channel client 'duplex-client-crash'
        And send request
        And simulate crash after '1' second
        
        Then the server is 'csharp'
        And complete processing after '2' seconds
        And attempt to send response
        And detect client death when writing
        And continue processing other requests
        
    Scenario: Test 14.10 - Channel Cleanup on Dispose
        Given the server is 'csharp'
        And create duplex channel 'duplex-cleanup' with default config
        And start echo handler
        
        When the client is 'python'
        And create duplex channel client 'duplex-cleanup'
        And send '5' requests
        And receive '3' responses
        
        Then the server is 'csharp'
        And dispose server while '2' responses pending
        
        When the client is 'python'
        And attempt to receive pending responses
        
        Then client should receive exception on pending
        And all shared memory should be cleaned up
        
        When new server is 'cpp'
        And create duplex channel 'duplex-cleanup'
        
        Then new server should reuse same channel name successfully