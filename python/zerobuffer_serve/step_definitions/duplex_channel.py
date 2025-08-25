"""
Step definitions for duplex channel tests
"""

import time
import threading
from typing import Optional, Dict, List, Any
import asyncio

from zerobuffer import BufferConfig, Frame, Writer
from zerobuffer.duplex import (
    DuplexChannelFactory,
    ImmutableDuplexServer,
    DuplexClient,
    ProcessingMode
)
from zerobuffer.exceptions import (
    ZeroBufferException,
    ReaderDeadException,
    WriterDeadException
)
from zerobuffer_serve.test_data_patterns import TestDataPatterns
from zerobuffer_serve.step_definitions.base import BaseSteps, given, when, then
from zerobuffer_serve.services import BufferNamingService


class DuplexChannelSteps(BaseSteps):
    """Step definitions for duplex channel scenarios"""
    
    def __init__(self, test_context: Any, logger: Any) -> None:
        super().__init__(test_context, logger)
        self._servers: Dict[str, Any] = {}
        self._clients: Dict[str, Any] = {}
        self._sent_requests: Dict[int, bytes] = {}
        self._received_responses: List[tuple] = []
        self._last_exception: Optional[Exception] = None
        self._response_time: Optional[float] = None
        self._measurement_start: Optional[float] = None
        self._buffer_naming = BufferNamingService(self.logger)
        
    @given(r"the '([^']+)' process creates immutable duplex channel '([^']+)' with metadata size '(\d+)' and payload size '(\d+)'")
    async def create_immutable_duplex_channel(self, process: str, channel_name: str, metadata_size: str, payload_size: str) -> None:
        """Create an immutable duplex channel with specified buffer sizes"""
        actual_channel_name = self._buffer_naming.get_buffer_name(channel_name)
        config = BufferConfig(
            metadata_size=int(metadata_size),
            payload_size=int(payload_size)
        )
        
        factory = DuplexChannelFactory()
        server = factory.create_immutable_server(actual_channel_name, config)
        self._servers[channel_name] = server
        
        self.logger.info(f"Created immutable duplex channel '{channel_name}' (actual: '{actual_channel_name}')")
        
    @given(r"the '([^']+)' process creates immutable duplex channel '([^']+)' with default config")
    async def create_immutable_duplex_channel_default(self, process: str, channel_name: str) -> None:
        """Create an immutable duplex channel with default configuration"""
        actual_channel_name = self._buffer_naming.get_buffer_name(channel_name)
        config = BufferConfig(4096, 1024 * 1024)  # Default: 4KB metadata, 1MB payload
        
        factory = DuplexChannelFactory()
        server = factory.create_immutable_server(actual_channel_name, config)
        self._servers[channel_name] = server
        
        self.logger.info(f"Created immutable duplex channel '{channel_name}' with default config")
        
    @given(r"the '([^']+)' process creates immutable duplex channel '([^']+)' with processing mode '([^']+)'")
    async def create_immutable_duplex_channel_with_mode(self, process: str, channel_name: str, mode: str) -> None:
        """Create an immutable duplex channel with specified processing mode"""
        actual_channel_name = self._buffer_naming.get_buffer_name(channel_name)
        config = BufferConfig(4096, 1024 * 1024)
        
        factory = DuplexChannelFactory()
        # v1.0.0: Only immutable server is supported
        # Processing mode is specified when starting the handler
        server = factory.create_immutable_server(actual_channel_name, config)
        self._servers[channel_name] = server
        
        self.logger.info(f"Created immutable duplex channel '{channel_name}' for mode '{mode}'")
        
    @given(r"the '([^']+)' process starts echo handler")
    async def start_echo_handler(self, process: str) -> None:
        """Start an echo handler that returns the same data"""
        server = list(self._servers.values())[-1]
        if not isinstance(server, ImmutableDuplexServer):
            raise TypeError(f"Expected ImmutableDuplexServer, got {type(server)}")
            
        def echo_handler(frame: Frame, writer: Writer) -> None:
            """Echo handler - return the same data"""
            with writer.get_frame_buffer(len(frame.data)) as buffer:
                buffer[:] = frame.data
            writer.commit_frame()
            
        server.start(echo_handler, mode=ProcessingMode.SINGLE_THREAD)
        
        self.logger.info("Started echo handler")
        
    @given(r"the '([^']+)' process starts handler with '(\d+)' ms processing time")
    async def start_delayed_handler(self, process: str, delay_ms: str) -> None:
        """Start a handler with specified processing delay"""
        server = list(self._servers.values())[-1]
        if not isinstance(server, ImmutableDuplexServer):
            raise TypeError(f"Expected ImmutableDuplexServer, got {type(server)}")
            
        delay = int(delay_ms) / 1000.0  # Convert to seconds
        
        def delayed_handler(frame: Frame, writer: Writer) -> None:
            """Handler with processing delay"""
            time.sleep(delay)
            with writer.get_frame_buffer(len(frame.data)) as buffer:
                buffer[:] = frame.data
            writer.commit_frame()
            
        server.start(delayed_handler, mode=ProcessingMode.SINGLE_THREAD)
        
        self.logger.info(f"Started handler with {delay_ms}ms delay")
        
    @given(r"the '([^']+)' process starts handler that doubles request size")
    async def start_doubling_handler(self, process: str) -> None:
        """Start a handler that doubles the request size"""
        server = list(self._servers.values())[-1]
        if not isinstance(server, ImmutableDuplexServer):
            raise TypeError(f"Expected ImmutableDuplexServer, got {type(server)}")
            
        def doubling_handler(frame: Frame, writer: Writer) -> None:
            """Handler that doubles the data"""
            data = bytes(frame.data)
            doubled = data + data  # Double the data
            with writer.get_frame_buffer(len(doubled)) as buffer:
                buffer[:] = doubled
            writer.commit_frame()
            
        server.start(doubling_handler, mode=ProcessingMode.SINGLE_THREAD)
        
        self.logger.info("Started doubling handler")
        
    @when(r"the '([^']+)' process creates duplex channel client '([^']+)'")
    async def create_duplex_client(self, process: str, channel_name: str) -> None:
        """Create a duplex channel client"""
        actual_channel_name = self._buffer_naming.get_buffer_name(channel_name)
        
        factory = DuplexChannelFactory()
        client = factory.create_client(actual_channel_name)
        self._clients[channel_name] = client
        
        self.logger.info(f"Created duplex channel client for '{channel_name}'")
        
    @when(r"the '([^']+)' process sends request with size '(\d+)'")
    async def send_request_with_size(self, process: str, size: str) -> None:
        """Send a request with specified size"""
        client = list(self._clients.values())[-1]
        if not isinstance(client, DuplexClient):
            raise TypeError(f"Expected DuplexClient, got {type(client)}")
            
        data_size = int(size)
        data = TestDataPatterns.generate_simple_frame_data(data_size)
        
        sequence = client.send_request(data)
        self._sent_requests[sequence] = data
        
        self.logger.info(f"Sent request with size {data_size}, sequence {sequence}")
        
    @when(r"the '([^']+)' process sends '(\d+)' requests sequentially")
    async def send_requests_sequentially(self, process: str, count: str) -> None:
        """Send multiple requests sequentially"""
        client = list(self._clients.values())[-1]
        if not isinstance(client, DuplexClient):
            raise TypeError(f"Expected DuplexClient, got {type(client)}")
            
        request_count = int(count)
        
        for i in range(request_count):
            data = f"Request {i}".encode()
            sequence = client.send_request(data)
            self._sent_requests[sequence] = data
            
        self.logger.info(f"Sent {request_count} requests sequentially")
        
    @when(r"the '([^']+)' process sends '(\d+)' requests from single thread")
    async def send_requests_single_thread(self, process: str, count: str) -> None:
        """Send multiple requests from a single thread"""
        # Same as sequential for v1.0.0
        await self.send_requests_sequentially(process, count)
        
    @when(r"the '([^']+)' process measures total response time")
    async def measure_response_time(self, process: str) -> None:
        """Measure total time to receive all responses"""
        client = list(self._clients.values())[-1]
        if not isinstance(client, DuplexClient):
            raise TypeError(f"Expected DuplexClient, got {type(client)}")
            
        self._measurement_start = time.time()
        
        # Receive all responses
        for _ in self._sent_requests:
            response = client.receive_response(timeout_ms=10000)
            if response.is_valid:
                with response:  # Use context manager for RAII
                    if response.data is not None:
                        self._received_responses.append((response.sequence, bytes(response.data)))
                    else:
                        self._received_responses.append((response.sequence, b''))
                    
        self._response_time = time.time() - self._measurement_start
        
        self.logger.info(f"Total response time: {self._response_time * 1000:.2f}ms")
        
    @then(r"response should match request with size '(\d+)'")
    async def verify_response_matches_request(self, size: str) -> None:
        """Verify that response matches the request"""
        client = list(self._clients.values())[-1]
        if not isinstance(client, DuplexClient):
            raise TypeError(f"Expected DuplexClient, got {type(client)}")
            
        response = client.receive_response(timeout_ms=5000)
        
        assert response.is_valid, "Response should be valid"
        
        with response:  # Use context manager for RAII
            assert response.data is not None, "Response data should not be None"
            assert len(response.data) == int(size), f"Response size mismatch: expected {size}, got {len(response.data)}"
            
            # Verify data matches if we have the original request
            if response.sequence in self._sent_requests:
                original_data = self._sent_requests[response.sequence]
                assert response.data is not None, "Response data should not be None"
                response_data = bytes(response.data)
                assert response_data == original_data, "Response data doesn't match request"
                
            # response.data is guaranteed to be not None here due to assertions above
            self._received_responses.append((response.sequence, bytes(response.data)))
            
        self.logger.info(f"Response matched request with size {size}")
        
    @then(r"the '([^']+)' process receives '(\d+)' responses in order")
    async def receive_responses_in_order(self, process: str, count: str) -> None:
        """Receive specified number of responses and verify order"""
        client = list(self._clients.values())[-1]
        if not isinstance(client, DuplexClient):
            raise TypeError(f"Expected DuplexClient, got {type(client)}")
            
        expected_count = int(count)
        self._received_responses.clear()
        
        for i in range(expected_count):
            response = client.receive_response(timeout_ms=10000)
            assert response.is_valid, f"Response {i} should be valid"
            
            with response:  # Use context manager for RAII
                if response.data is not None:
                    self._received_responses.append((response.sequence, bytes(response.data)))
                else:
                    self._received_responses.append((response.sequence, b''))
                
        assert len(self._received_responses) == expected_count, \
            f"Expected {expected_count} responses, got {len(self._received_responses)}"
            
        # Verify order (sequences should be increasing)
        sequences = [seq for seq, _ in self._received_responses]
        sorted_sequences = sorted(sequences)
        assert sequences == sorted_sequences, "Responses not in order"
        
        self.logger.info(f"Received {expected_count} responses in order")
        
    @then(r"responses should match requests by content")
    async def verify_responses_match_content(self) -> None:
        """Verify that response content matches request content"""
        for sequence, response_data in self._received_responses:
            if sequence in self._sent_requests:
                request_data = self._sent_requests[sequence]
                assert response_data == request_data, \
                    f"Response for sequence {sequence} doesn't match request"
                    
        self.logger.info("All responses match their requests by content")
        
    @then(r"no responses should be lost or duplicated")
    async def verify_no_lost_or_duplicate_responses(self) -> None:
        """Verify no responses are lost or duplicated"""
        received_sequences = [seq for seq, _ in self._received_responses]
        
        # Check for duplicates
        assert len(received_sequences) == len(set(received_sequences)), \
            "Duplicate responses detected"
            
        # Check all sent requests have responses
        for sent_seq in self._sent_requests.keys():
            assert sent_seq in received_sequences, \
                f"Response for sequence {sent_seq} was lost"
                
        self.logger.info("No responses lost or duplicated")
        
    @then(r"the '([^']+)' process receives exactly '(\d+)' responses")
    async def receive_exact_response_count(self, process: str, count: str) -> None:
        """Verify exact number of responses received"""
        expected_count = int(count)
        assert len(self._received_responses) == expected_count, \
            f"Expected {expected_count} responses, got {len(self._received_responses)}"
            
        self.logger.info(f"Received exactly {expected_count} responses")
        
    @then(r"all '(\d+)' responses match their requests")
    async def verify_all_responses_match(self, count: str) -> None:
        """Verify all responses match their requests"""
        expected_count = int(count)
        assert len(self._received_responses) == expected_count, \
            f"Expected {expected_count} responses, got {len(self._received_responses)}"
            
        await self.verify_responses_match_content()
        
    @then(r"total time should be at least '(\d+)' ms")
    async def verify_minimum_response_time(self, min_ms: str) -> None:
        """Verify total response time is at least the specified duration"""
        min_time = int(min_ms) / 1000.0  # Convert to seconds
        
        assert self._response_time is not None, "Response time not measured"
        assert self._response_time >= min_time, \
            f"Response time {self._response_time * 1000:.2f}ms is less than minimum {min_ms}ms"
            
        self.logger.info(f"Response time {self._response_time * 1000:.2f}ms >= {min_ms}ms")
        
    @then(r"responses should arrive in order")
    async def verify_responses_arrive_in_order(self) -> None:
        """Verify responses arrive in order"""
        sequences = [seq for seq, _ in self._received_responses]
        sorted_sequences = sorted(sequences)
        assert sequences == sorted_sequences, "Responses did not arrive in order"
        
        self.logger.info("Responses arrived in order")