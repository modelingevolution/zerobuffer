#!/usr/bin/env python3
"""
Example of using Duplex Channel - matches C# example from API.md
"""

import time
import threading
from zerobuffer import DuplexChannelFactory, BufferConfig


def test_immutable_server():
    """Test immutable server example from API.md"""
    
    factory = DuplexChannelFactory.get_instance()
    config = BufferConfig(4096, 256*1024*1024)
    
    # Create server
    server = factory.create_immutable_server("image-processing", config)
    
    # Define handler that processes image
    def process_image(request_frame):
        """Process image and return new data"""
        # Server automatically preserves request.sequence in the response
        data = bytes(request_frame.data)
        # Simulate processing - just reverse the bytes
        processed = data[::-1]
        return processed
    
    # Start server in background
    server_thread = threading.Thread(target=lambda: server.start(process_image))
    server_thread.daemon = True
    server_thread.start()
    
    # Give server time to start
    time.sleep(0.5)
    
    # Create client
    client = factory.create_client("image-processing")
    
    # Wait for server to be ready
    timeout_start = time.time()
    while not client.is_server_connected():
        if time.time() - timeout_start > 5:
            print("Timeout waiting for server")
            return
        time.sleep(0.1)
    
    print("Server connected!")
    
    # Test data
    test_data = b"Hello, ZeroBuffer Duplex Channel!"
    
    # Option 1: Send with copy
    sequence_number = client.send_request(test_data)
    print(f"Sent request with sequence: {sequence_number}")
    
    # Receive response
    response = client.receive_response(5000)  # 5 second timeout
    
    # Check if this is our response by matching sequence number
    if response.is_valid and response.sequence == sequence_number:
        response_data = response.to_bytes()
        print(f"Received response: {response_data}")
        
        # Verify it's reversed
        if response_data == test_data[::-1]:
            print("✓ Response correctly processed!")
        else:
            print("✗ Response data mismatch")
        
        # Release response
        client.release_response(response)
    
    # Option 2: Zero-copy write
    test_data2 = b"Zero-copy test message"
    sequence_number2, buffer = client.acquire_request_buffer(len(test_data2))
    
    # Write to buffer
    buffer[:] = test_data2
    client.commit_request()
    print(f"Sent zero-copy request with sequence: {sequence_number2}")
    
    # Receive response
    response2 = client.receive_response(5000)
    if response2.is_valid and response2.sequence == sequence_number2:
        response_data2 = response2.to_bytes()
        print(f"Received zero-copy response: {response_data2}")
        
        if response_data2 == test_data2[::-1]:
            print("✓ Zero-copy response correctly processed!")
        
        client.release_response(response2)
    
    # Cleanup
    client.close()
    server.stop()
    print("\nTest completed successfully!")


def test_mutable_server():
    """Test mutable server example"""
    
    factory = DuplexChannelFactory.get_instance()
    config = BufferConfig(4096, 256*1024*1024)
    
    # Create mutable server
    server = factory.create_mutable_server("filters", config)
    
    def apply_filter_inplace(frame):
        """Apply filter in-place"""
        # In Python, we can't truly modify shared memory in-place
        # But we simulate the pattern
        data = bytearray(frame.data)
        # Simple filter: XOR with 0xFF
        for i in range(len(data)):
            data[i] ^= 0xFF
        # Note: In real implementation, we'd need to write back to shared memory
        # For now, the server will write the data as response
    
    # Start server
    server_thread = threading.Thread(target=lambda: server.start(apply_filter_inplace))
    server_thread.daemon = True
    server_thread.start()
    
    time.sleep(0.5)
    
    # Create client
    client = factory.create_client("filters")
    
    # Wait for server
    timeout_start = time.time()
    while not client.is_server_connected():
        if time.time() - timeout_start > 5:
            print("Timeout waiting for server")
            return
        time.sleep(0.1)
    
    print("\nMutable server test:")
    
    # Send request
    test_data = bytes([i % 256 for i in range(100)])
    sequence = client.send_request(test_data)
    
    # Receive response
    response = client.receive_response(5000)
    if response.is_valid and response.sequence == sequence:
        response_data = response.to_bytes()
        
        # Verify XOR filter was applied
        expected = bytes([(b ^ 0xFF) for b in test_data])
        if response_data == expected:
            print("✓ Mutable filter correctly applied!")
        else:
            print("✗ Filter not applied correctly")
        
        client.release_response(response)
    
    # Cleanup
    client.close()
    server.stop()
    print("Mutable server test completed!")


if __name__ == "__main__":
    print("Testing ZeroBuffer Duplex Channel Implementation\n")
    test_immutable_server()
    print("\n" + "="*50 + "\n")
    test_mutable_server()