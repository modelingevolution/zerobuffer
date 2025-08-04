#!/usr/bin/env python3
"""
Simple test to debug duplex channel
"""

import time
import threading
import logging
from zerobuffer import DuplexChannelFactory, BufferConfig, setup_logging, ProcessingMode

# Enable info logging
setup_logging(level="INFO")


def test_simple():
    import os
    factory = DuplexChannelFactory.get_instance()
    config = BufferConfig(4096, 10 * 1024 * 1024)
    
    channel_name = f"test_simple_{os.getpid()}"
    
    # Create server
    server = factory.create_immutable_server(channel_name, config)
    
    def echo_handler(frame):
        print(f"Server: Received frame seq={frame.sequence}, size={frame.size}")
        data = bytes(frame.data)
        print(f"Server: Returning {len(data)} bytes")
        return data
    
    # Start server in a background thread
    print("Starting server...")
    server.start(echo_handler, ProcessingMode.SINGLE_THREAD)
    
    time.sleep(0.5)
    
    # Create client
    print("Client: Creating...")
    client = factory.create_client(channel_name)
    
    # Check if server is connected
    print(f"Client: Server connected = {client.is_server_connected}")
    
    # Send request
    print("Client: Sending request...")
    test_data = b"Hello!"
    seq = client.send_request(test_data)
    print(f"Client: Sent with sequence {seq}")
    
    # Receive response
    print("Client: Waiting for response...")
    response = client.receive_response(5000)
    print(f"Client: Got response valid={response.is_valid}, seq={response.sequence}")
    
    if response.is_valid:
        print(f"Client: Response data: {response.to_bytes()}")
    
    # Cleanup
    print("Test completed successfully!")
    client.close()
    server.stop()
    time.sleep(0.5)  # Give server time to stop
    print("Done!")


if __name__ == "__main__":
    test_simple()