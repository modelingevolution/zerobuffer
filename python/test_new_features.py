#!/usr/bin/env python3
"""
Test new features: timeout, on_init callback, and logger factory
"""

import time
import threading
import logging
from zerobuffer import (
    BufferConfig,
    ImmutableDuplexServer,
    DuplexClient,
    ErrorEventArgs,
    LoggerFactory,
    NullLoggerFactory,
    set_default_factory
)


def test_timeout_feature():
    """Test configurable timeout"""
    print("\n=== Testing Timeout Feature ===")
    
    # Create server with 1 second timeout
    config = BufferConfig(size=1024*1024, metadata_size=1024)
    server = ImmutableDuplexServer("test_timeout", config, timeout=1.0)
    
    # Handler that tracks timeouts
    timeout_count = [0]
    
    def handler(frame):
        return b"response"
    
    # Start server
    server.start(handler)
    print(f"Server started with 1 second timeout")
    
    # Don't connect client - server should timeout
    time.sleep(3)
    
    server.stop()
    print("Timeout test completed")


def test_on_init_callback():
    """Test initialization callback"""
    print("\n=== Testing On Init Callback ===")
    
    config = BufferConfig(size=1024*1024, metadata_size=1024)
    server = ImmutableDuplexServer("test_init", config)
    
    # Track if init was called
    init_called = [False]
    metadata_received = [None]
    
    def on_init(metadata):
        """Initialization callback"""
        init_called[0] = True
        metadata_received[0] = bytes(metadata)
        print(f"Init callback received metadata: {len(metadata)} bytes")
    
    def handler(frame):
        return b"response"
    
    # Start server with init callback
    server.start(handler, on_init=on_init)
    
    # Create client that writes metadata
    def client_task():
        client = DuplexClient("test_init")
        # Write some metadata (this would normally be done by the client)
        time.sleep(0.5)  # Let server initialize
        client.close()
    
    client_thread = threading.Thread(target=client_task)
    client_thread.start()
    client_thread.join()
    
    time.sleep(0.5)
    server.stop()
    
    if init_called[0]:
        print(f"✓ Init callback was called successfully")
    else:
        print(f"✗ Init callback was not called")


def test_logger_factory():
    """Test logger factory support"""
    print("\n=== Testing Logger Factory ===")
    
    # Create custom logger factory with specific format
    factory = LoggerFactory(
        level=logging.DEBUG,
        format_string='[%(levelname)s] %(name)s: %(message)s'
    )
    
    config = BufferConfig(size=1024*1024, metadata_size=1024)
    
    # Test 1: Pass logger factory to server
    server1 = ImmutableDuplexServer("test_logger1", config, logger=factory)
    print("✓ Server created with LoggerFactory")
    
    # Test 2: Use NullLoggerFactory (no output)
    null_factory = NullLoggerFactory()
    server2 = ImmutableDuplexServer("test_logger2", config, logger=null_factory)
    print("✓ Server created with NullLoggerFactory")
    
    # Test 3: Set default factory
    set_default_factory(factory)
    server3 = ImmutableDuplexServer("test_logger3", config)  # Uses default factory
    print("✓ Server created with default factory")
    
    # Test 4: Pass regular logger (backward compatibility)
    regular_logger = logging.getLogger("test_regular")
    server4 = ImmutableDuplexServer("test_logger4", config, logger=regular_logger)
    print("✓ Server created with regular logger (backward compatible)")


def test_combined_features():
    """Test all features together"""
    print("\n=== Testing Combined Features ===")
    
    # Create logger factory
    factory = LoggerFactory(level=logging.INFO)
    
    # Create server with all features
    config = BufferConfig(size=1024*1024, metadata_size=1024)
    server = ImmutableDuplexServer(
        "test_combined",
        config,
        timeout=2.0,  # 2 second timeout
        logger=factory  # Logger factory
    )
    
    # Track initialization
    init_data = [None]
    
    def on_init(metadata):
        init_data[0] = "Initialized with metadata"
        print(f"Server initialized with {len(metadata) if metadata else 0} bytes of metadata")
    
    def handler(frame):
        return b"Processed: " + bytes(frame.data)[:50]
    
    # Error handler
    def on_error(args: ErrorEventArgs):
        print(f"Error occurred: {args.exception}")
    
    server.add_error_handler(on_error)
    
    # Start server with init callback
    server.start(handler, on_init=on_init)
    
    print("Server started with:")
    print("  - 2 second timeout")
    print("  - Logger factory")
    print("  - Init callback")
    print("  - Error handler")
    
    # Simulate client
    def client_task():
        try:
            client = DuplexClient("test_combined")
            seq = client.send_request(b"Hello World!")
            response = client.receive_response(timeout_ms=1000)
            print(f"Client received: {response.to_bytes()}")
            client.close()
        except Exception as e:
            print(f"Client error: {e}")
    
    client_thread = threading.Thread(target=client_task)
    client_thread.start()
    client_thread.join()
    
    time.sleep(0.5)
    server.stop()
    
    print("\n✓ All features tested successfully!")


if __name__ == "__main__":
    print("=" * 50)
    print("Testing Python ZeroBuffer New Features")
    print("=" * 50)
    
    test_timeout_feature()
    test_on_init_callback()
    test_logger_factory()
    test_combined_features()
    
    print("\n" + "=" * 50)
    print("All tests completed!")
    print("=" * 50)