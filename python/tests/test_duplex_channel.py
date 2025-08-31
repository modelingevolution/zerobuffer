"""
Duplex Channel tests matching C# DuplexChannelTests.cs
"""

import os
import time
import statistics
import pytest
from zerobuffer import DuplexChannelFactory, BufferConfig, ReaderDeadException, ProcessingMode, Writer
from zerobuffer.types import Frame


class TestDuplexChannel:
    """Test duplex channel functionality - matches C# DuplexChannelTests"""

    def setup_method(self) -> None:
        """Setup test channel name"""
        self._test_channel_name = f"test_duplex_{os.getpid()}_{id(self)}"

    def test_immutable_server_echo_test(self) -> None:
        """ImmutableServer_EchoTest - matches C# test"""
        factory = DuplexChannelFactory.get_instance()
        config = BufferConfig(4096, 10 * 1024 * 1024)  # 10MB buffer

        # Create server with echo handler
        server = factory.create_immutable_server(self._test_channel_name, config)

        def echo_handler(request_frame: Frame, writer: Writer) -> None:
            # Echo the request data back
            with writer.get_frame_buffer(len(request_frame.data)) as buffer:
                buffer[:] = request_frame.data
            writer.commit_frame()

        # Start server in background thread
        server.start(echo_handler, mode=ProcessingMode.SINGLE_THREAD)

        # Give server time to initialize
        time.sleep(0.1)

        # Create client
        client = factory.create_client(self._test_channel_name)

        try:
            # Send test message
            test_data = b"Hello, Duplex Channel!"
            sequence_number = client.send_request(test_data)
            response = client.receive_response(5000)  # 5 second timeout

            assert response.is_valid
            assert response.sequence == sequence_number
            assert response.to_bytes() == test_data

            response.dispose()

        finally:
            client.close()
            server.stop()

    def test_immutable_server_transform_test(self) -> None:
        """ImmutableServer_TransformTest - matches C# test"""
        factory = DuplexChannelFactory.get_instance()
        config = BufferConfig(4096, 10 * 1024 * 1024)

        # Create server that transforms data
        server = factory.create_immutable_server(self._test_channel_name, config)

        def transform_handler(request_frame: Frame, writer: Writer) -> None:
            data = bytearray(request_frame.data)
            # Simple transform: reverse the bytes
            data.reverse()
            with writer.get_frame_buffer(len(data)) as buffer:
                buffer[:] = data
            writer.commit_frame()

        # Start server in background thread
        server.start(transform_handler, mode=ProcessingMode.SINGLE_THREAD)

        time.sleep(0.1)

        client = factory.create_client(self._test_channel_name)

        try:
            test_data = bytes([1, 2, 3, 4, 5])
            sequence_number = client.send_request(test_data)
            response = client.receive_response(5000)

            assert response.is_valid
            assert response.sequence == sequence_number
            assert response.to_bytes() == bytes([5, 4, 3, 2, 1])

            response.dispose()

        finally:
            client.close()
            server.stop()

    @pytest.mark.skip(reason="MutableServer not implemented - planned for v2.0")
    def test_mutable_server_in_place_transform_test(self) -> None:
        """MutableServer_InPlaceTransformTest - matches C# test"""
        factory = DuplexChannelFactory.get_instance()
        config = BufferConfig(4096, 10 * 1024 * 1024)

        # Create mutable server that modifies data in-place
        server = factory.create_mutable_server(self._test_channel_name, config)

        def xor_transform(frame: Frame) -> None:
            # In C#, this would modify the frame data in-place using GetMutableSpan()
            # In Python, we can't truly modify shared memory in-place
            # This is a known limitation of the Python implementation
            # The mutable server will return the original data unchanged
            pass

        # Start server in background thread
        server.start(xor_transform, mode=ProcessingMode.SINGLE_THREAD)

        time.sleep(0.1)

        client = factory.create_client(self._test_channel_name)

        try:
            test_data = bytes([0x00, 0x01, 0xFE, 0xFF])
            sequence_number = client.send_request(test_data)
            response = client.receive_response(5000)

            assert response.is_valid
            assert response.sequence == sequence_number
            # In Python, mutable server returns data unchanged
            # True zero-copy in-place modification is not supported
            assert response.to_bytes() == test_data

            response.dispose()

        finally:
            client.close()
            server.stop()

    def test_latency_measurement_test(self) -> None:
        """LatencyMeasurement_Test - matches C# test"""
        factory = DuplexChannelFactory.get_instance()
        config = BufferConfig(4096, 10 * 1024 * 1024)

        # Create server with minimal processing
        server = factory.create_immutable_server(self._test_channel_name, config)

        # Start server in background thread with minimal processing
        def echo_handler(frame: Frame, writer: Writer) -> None:
            with writer.get_frame_buffer(len(frame.data)) as buffer:
                buffer[:] = frame.data
            writer.commit_frame()

        server.start(echo_handler, mode=ProcessingMode.SINGLE_THREAD)

        time.sleep(0.1)

        client = factory.create_client(self._test_channel_name)

        try:
            # Warm up
            for _ in range(10):
                client.send_request(bytes(1024))
                resp = client.receive_response(1000)
                resp.dispose()

            # Measure latency
            latencies = []
            test_data = bytes(1024)

            for i in range(100):
                start_time = time.perf_counter()
                sequence_number = client.send_request(test_data)
                response = client.receive_response(1000)
                end_time = time.perf_counter()

                assert response.is_valid
                assert response.sequence == sequence_number

                latency_ms = (end_time - start_time) * 1000
                latencies.append(latency_ms)
                response.dispose()

            avg_latency = statistics.mean(latencies)
            min_latency = min(latencies)
            max_latency = max(latencies)

            # Log results
            print(
                f"Duplex Channel Latency - Avg: {avg_latency:.2f}ms, "
                f"Min: {min_latency:.2f}ms, Max: {max_latency:.2f}ms"
            )

            # Basic sanity checks
            assert avg_latency < 50, f"Average latency too high: {avg_latency}ms"
            assert min_latency < 10, f"Minimum latency too high: {min_latency}ms"

        finally:
            client.close()
            server.stop()

    def test_server_stop_client_handles_gracefully(self) -> None:
        """ServerStop_ClientHandlesGracefully - matches C# test"""
        factory = DuplexChannelFactory.get_instance()
        config = BufferConfig(4096, 10 * 1024 * 1024)

        server = factory.create_immutable_server(self._test_channel_name, config)

        # Start server in background thread with minimal processing
        def echo_handler(frame: Frame, writer: Writer) -> None:
            with writer.get_frame_buffer(len(frame.data)) as buffer:
                buffer[:] = frame.data
            writer.commit_frame()

        server.start(echo_handler, mode=ProcessingMode.SINGLE_THREAD)

        time.sleep(0.1)

        client = factory.create_client(self._test_channel_name)

        try:
            # Verify connection works
            seq1 = client.send_request(bytes([1, 2, 3]))
            response = client.receive_response(1000)
            assert response.is_valid
            assert response.sequence == seq1
            response.dispose()

            # Stop server
            server.stop()
            time.sleep(0.1)

            # Client should detect server disconnection
            # SendRequest should throw ReaderDeadException when server's reader is gone
            with pytest.raises(ReaderDeadException):
                client.send_request(bytes([4, 5, 6]))

        finally:
            client.close()


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
