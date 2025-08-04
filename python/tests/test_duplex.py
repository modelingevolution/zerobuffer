"""
Duplex channel tests for ZeroBuffer Python implementation

Tests bidirectional communication between processes
"""

import os
import time
import threading
import multiprocessing
import pytest
from zerobuffer import Reader, Writer, BufferConfig


class TestDuplexChannel:
    """Test bidirectional communication patterns"""
    
    def test_basic_duplex_communication(self):
        """Test basic request-response pattern"""
        request_buffer = f"duplex_req_{os.getpid()}"
        response_buffer = f"duplex_resp_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        def server_process():
            """Server that receives requests and sends responses"""
            # Create request buffer as reader
            with Reader(request_buffer, config) as req_reader:
                # Wait for client
                timeout_start = time.time()
                while time.time() - timeout_start < 5.0:
                    if req_reader.is_writer_connected():
                        break
                    time.sleep(0.1)
                else:
                    return
                
                # Connect to response buffer as writer
                with Writer(response_buffer) as resp_writer:
                    # Process requests
                    for _ in range(3):
                        frame = req_reader.read_frame(timeout=2.0)
                        if frame:
                            request_data = bytes(frame.data)
                            req_reader.release_frame(frame)
                            
                            # Echo back with prefix
                            response = b"RESPONSE: " + request_data
                            resp_writer.write_frame(response)
        
        def client_process():
            """Client that sends requests and receives responses"""
            # Create response buffer as reader first
            with Reader(response_buffer, config) as resp_reader:
                # Connect to request buffer as writer
                with Writer(request_buffer) as req_writer:
                    # Send requests and get responses
                    messages = [b"Hello", b"World", b"Duplex"]
                    responses = []
                    
                    for msg in messages:
                        # Send request
                        req_writer.write_frame(msg)
                        
                        # Get response
                        frame = resp_reader.read_frame(timeout=2.0)
                        if frame:
                            responses.append(bytes(frame.data))
                            resp_reader.release_frame(frame)
                    
                    return responses
        
        # Start server
        server = multiprocessing.Process(target=server_process)
        server.start()
        
        # Give server time to start
        time.sleep(0.5)
        
        # Run client
        try:
            responses = client_process()
            assert len(responses) == 3
            assert responses[0] == b"RESPONSE: Hello"
            assert responses[1] == b"RESPONSE: World"
            assert responses[2] == b"RESPONSE: Duplex"
        finally:
            server.terminate()
            server.join(timeout=2.0)
    
    def test_concurrent_duplex_channels(self):
        """Test multiple concurrent duplex channels"""
        num_channels = 3
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        def channel_worker(channel_id, results_queue):
            """Worker for a single duplex channel"""
            req_buffer = f"duplex_req_{channel_id}_{os.getpid()}"
            resp_buffer = f"duplex_resp_{channel_id}_{os.getpid()}"
            
            # Server side
            def server():
                with Reader(req_buffer, config) as req_reader:
                    timeout_start = time.time()
                    while time.time() - timeout_start < 5.0:
                        if req_reader.is_writer_connected():
                            break
                        time.sleep(0.1)
                    else:
                        return
                    
                    with Writer(resp_buffer) as resp_writer:
                        for i in range(5):
                            frame = req_reader.read_frame(timeout=2.0)
                            if frame:
                                data = bytes(frame.data)
                                req_reader.release_frame(frame)
                                resp_writer.write_frame(data + b"_ACK")
            
            # Start server in thread
            server_thread = threading.Thread(target=server)
            server_thread.start()
            time.sleep(0.2)
            
            # Client side
            try:
                with Reader(resp_buffer, config) as resp_reader:
                    with Writer(req_buffer) as req_writer:
                        success_count = 0
                        for i in range(5):
                            msg = f"CH{channel_id}_MSG{i}".encode()
                            req_writer.write_frame(msg)
                            
                            frame = resp_reader.read_frame(timeout=2.0)
                            if frame:
                                response = bytes(frame.data)
                                resp_reader.release_frame(frame)
                                if response == msg + b"_ACK":
                                    success_count += 1
                        
                        results_queue.put((channel_id, success_count))
            finally:
                server_thread.join(timeout=2.0)
        
        # Run multiple channels concurrently
        results_queue = multiprocessing.Queue()
        processes = []
        
        for i in range(num_channels):
            p = multiprocessing.Process(target=channel_worker, args=(i, results_queue))
            p.start()
            processes.append(p)
        
        # Collect results
        results = {}
        for _ in range(num_channels):
            channel_id, success_count = results_queue.get(timeout=10.0)
            results[channel_id] = success_count
        
        # Wait for processes
        for p in processes:
            p.join(timeout=2.0)
            if p.is_alive():
                p.terminate()
        
        # Verify all channels worked
        assert len(results) == num_channels
        for channel_id in range(num_channels):
            assert results[channel_id] == 5, f"Channel {channel_id} failed"
    
    def test_duplex_with_metadata_exchange(self):
        """Test duplex communication with metadata exchange"""
        req_buffer = f"duplex_meta_req_{os.getpid()}"
        resp_buffer = f"duplex_meta_resp_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64*1024)
        
        def server_process():
            """Server with metadata handling"""
            with Reader(req_buffer, config) as req_reader:
                timeout_start = time.time()
                while time.time() - timeout_start < 5.0:
                    if req_reader.is_writer_connected():
                        break
                    time.sleep(0.1)
                else:
                    return
                
                # Get client metadata
                client_meta = req_reader.get_metadata()
                client_info = bytes(client_meta).decode() if client_meta else "Unknown"
                
                with Writer(resp_buffer) as resp_writer:
                    # Set server metadata
                    server_meta = f"Server v1.0, received from: {client_info}"
                    resp_writer.set_metadata(server_meta.encode())
                    
                    # Echo frames with modification
                    for _ in range(3):
                        frame = req_reader.read_frame(timeout=2.0)
                        if frame:
                            data = bytes(frame.data)
                            req_reader.release_frame(frame)
                            
                            response = f"[{client_info}] {data.decode()}".encode()
                            resp_writer.write_frame(response)
        
        # Start server
        server = multiprocessing.Process(target=server_process)
        server.start()
        time.sleep(0.5)
        
        try:
            # Client
            with Reader(resp_buffer, config) as resp_reader:
                with Writer(req_buffer) as req_writer:
                    # Set client metadata
                    req_writer.set_metadata(b"TestClient v2.0")
                    
                    # Wait for server connection and metadata
                    timeout_start = time.time()
                    while time.time() - timeout_start < 5.0:
                        if resp_reader.is_writer_connected():
                            break
                        time.sleep(0.1)
                    else:
                        pytest.fail("Server didn't connect")
                    
                    server_meta = resp_reader.get_metadata()
                    assert server_meta is not None
                    assert b"Server v1.0" in bytes(server_meta)
                    assert b"TestClient v2.0" in bytes(server_meta)
                    
                    # Exchange messages
                    messages = [b"First", b"Second", b"Third"]
                    for msg in messages:
                        req_writer.write_frame(msg)
                        
                        frame = resp_reader.read_frame(timeout=2.0)
                        assert frame is not None
                        response = bytes(frame.data)
                        resp_reader.release_frame(frame)
                        
                        expected = f"[TestClient v2.0] {msg.decode()}".encode()
                        assert response == expected
        finally:
            server.terminate()
            server.join(timeout=2.0)


if __name__ == "__main__":
    multiprocessing.set_start_method('spawn', force=True)
    pytest.main([__file__, "-v", "-s"])