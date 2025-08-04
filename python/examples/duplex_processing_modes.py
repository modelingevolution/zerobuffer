#!/usr/bin/env python3
"""
Example demonstrating ProcessingMode usage in Duplex Channel servers
"""

from zerobuffer import (
    DuplexChannelFactory, BufferConfig, ProcessingMode, setup_logging
)
import time
import threading

# Enable logging to see what's happening
setup_logging(level="INFO")


def example_single_thread():
    """Example using SINGLE_THREAD mode (default)"""
    print("\n=== SINGLE_THREAD Mode Example ===")
    
    factory = DuplexChannelFactory.get_instance()
    config = BufferConfig(4096, 10 * 1024 * 1024)
    
    server = factory.create_immutable_server("single_thread_channel", config)
    
    def slow_handler(frame):
        # Simulate slow processing
        print(f"[Server] Processing frame {frame.sequence} in thread {threading.current_thread().name}")
        time.sleep(0.1)  # 100ms processing time
        return b"Processed: " + bytes(frame.data)
    
    # Start with SINGLE_THREAD mode (default)
    # All requests are processed sequentially in one background thread
    server.start(slow_handler, ProcessingMode.SINGLE_THREAD)
    
    # Create client and send multiple requests
    client = factory.create_client("single_thread_channel")
    
    start_time = time.time()
    
    # Send 5 requests
    for i in range(5):
        seq = client.send_request(f"Request {i}".encode())
        print(f"[Client] Sent request {seq}")
    
    # Receive all responses
    for i in range(5):
        response = client.receive_response(5000)
        print(f"[Client] Got response {response.sequence}: {response.to_bytes().decode()}")
        client.release_response(response)
    
    elapsed = time.time() - start_time
    print(f"\nTotal time for 5 requests: {elapsed:.2f}s")
    print("Note: Requests were processed sequentially (~0.5s total)")
    
    client.close()
    server.stop()


def example_thread_pool():
    """Example showing what THREAD_POOL mode would do (not yet implemented)"""
    print("\n=== THREAD_POOL Mode Example (Not Yet Implemented) ===")
    
    factory = DuplexChannelFactory.get_instance()
    config = BufferConfig(4096, 10 * 1024 * 1024)
    
    server = factory.create_immutable_server("thread_pool_channel", config)
    
    def slow_handler(frame):
        # This would run in different threads from a pool
        print(f"[Server] Would process frame {frame.sequence} in thread pool")
        time.sleep(0.1)
        return b"Processed: " + bytes(frame.data)
    
    try:
        # This will raise NotImplementedError
        server.start(slow_handler, ProcessingMode.THREAD_POOL)
    except NotImplementedError as e:
        print(f"Expected error: {e}")
        print("\nWhen implemented, THREAD_POOL mode would:")
        print("- Process each request in a separate thread from a pool")
        print("- Allow concurrent processing of multiple requests")
        print("- Complete 5 requests in ~0.1s (if pool size >= 5)")


if __name__ == "__main__":
    example_single_thread()
    example_thread_pool()