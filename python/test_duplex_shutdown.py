#!/usr/bin/env python3
"""
Test duplex channel shutdown
"""

import os
import time
import threading
from zerobuffer import DuplexChannelFactory, BufferConfig, ProcessingMode


def test_shutdown():
    factory = DuplexChannelFactory.get_instance()
    config = BufferConfig(4096, 10 * 1024 * 1024)
    
    channel_name = f"test_shutdown_{os.getpid()}"
    
    # Create server
    server = factory.create_immutable_server(channel_name, config)
    
    def echo_handler(frame):
        return bytes(frame.data)
    
    # Start server in background thread
    print("Starting server in background thread...")
    server.start(echo_handler, ProcessingMode.SINGLE_THREAD)
    
    # Give server time to start
    time.sleep(0.2)
    
    # Create client and send one message
    client = factory.create_client(channel_name)
    
    # Send and receive one message
    seq = client.send_request(b"test")
    resp = client.receive_response(1000)
    print(f"Got response: seq={resp.sequence}, valid={resp.is_valid}")
    client.release_response(resp)
    
    # Now test shutdown
    print("Stopping server...")
    start_time = time.time()
    server.stop()
    stop_time = time.time()
    
    print(f"Server stopped in {stop_time - start_time:.2f} seconds")
    
    # Clean up client
    client.close()
    
    # Server should be stopped now
    print("SUCCESS: Shutdown completed")


if __name__ == "__main__":
    test_shutdown()