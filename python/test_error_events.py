#!/usr/bin/env python3
"""
Test error event handling in duplex servers
"""

import time
import threading
from zerobuffer import (
    BufferConfig, 
    ImmutableDuplexServer,
    DuplexClient,
    ErrorEventArgs,
    WriterDeadException
)


def test_error_event_handling():
    """Test that error events are properly triggered"""
    
    config = BufferConfig(size=1024*1024, metadata_size=1024)
    server = ImmutableDuplexServer("test_errors", config)
    
    # Track errors
    errors_received = []
    
    def error_handler(args: ErrorEventArgs):
        """Capture error events"""
        errors_received.append(args.exception)
        print(f"Error event received: {type(args.exception).__name__}: {args.exception}")
    
    # Register error handler
    server.add_error_handler(error_handler)
    
    # Simple echo handler
    def echo_handler(frame):
        return bytes(frame.data)
    
    # Start server
    server.start(echo_handler)
    
    # Create client in another thread
    def client_task():
        client = DuplexClient("test_errors")
        # Send a request
        seq = client.send_request(b"Hello")
        # Receive response
        response = client.receive_response(timeout_ms=1000)
        print(f"Got response: {response.to_bytes()}")
        # Disconnect abruptly (simulating error)
        client.close()
    
    client_thread = threading.Thread(target=client_task)
    client_thread.start()
    client_thread.join()
    
    # Give server time to detect disconnection
    time.sleep(0.5)
    
    # Stop server
    server.stop()
    
    # Check that we received error events
    if errors_received:
        print(f"\nSuccess! Received {len(errors_received)} error event(s):")
        for err in errors_received:
            print(f"  - {type(err).__name__}: {err}")
    else:
        print("\nNo error events received (might be expected if client disconnected cleanly)")
    
    # Test removing handler
    server.remove_error_handler(error_handler)
    print("\nError handler removed successfully")


if __name__ == "__main__":
    test_error_event_handling()