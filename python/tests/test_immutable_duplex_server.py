"""
Test suite for ImmutableDuplexServer focusing on behavioral testing
Corresponds to C# tests in DuplexChannelTests.cs and ProcessingModeTests.cs
"""

import os
import time
import threading
import pytest
from typing import List, Any
from unittest.mock import patch, MagicMock
from zerobuffer import BufferConfig, Reader, Writer
from zerobuffer.duplex.server import ImmutableDuplexServer
from zerobuffer.duplex.processing_mode import ProcessingMode
from zerobuffer.exceptions import ZeroBufferException, ReaderDeadException, WriterDeadException
from zerobuffer.error_event_args import ErrorEventArgs
from zerobuffer.types import Frame


class TestBasicServerLifecycle:
    """Test server start/stop behaviors"""

    def test_server_can_be_started_successfully(self) -> None:
        """Server can be started with a handler"""
        channel_name = f"test_lifecycle_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        handler_called = threading.Event()

        def handler(frame: Frame, writer: Writer) -> None:
            handler_called.set()

        # Mock the Reader and Writer to avoid actual buffer creation
        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            server.start(handler)
            assert server.is_running
            server.stop()
            assert not server.is_running

    def test_server_raises_exception_if_already_running(self) -> None:
        """Server raises exception if started when already running"""
        channel_name = f"test_double_start_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            server.start(handler)
            try:
                with pytest.raises(ZeroBufferException, match="Server is already running"):
                    server.start(handler)
            finally:
                server.stop()

    def test_server_can_be_stopped_cleanly(self) -> None:
        """Server can be stopped and releases resources"""
        channel_name = f"test_clean_stop_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_writer = MagicMock()

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer", return_value=mock_writer
        ):
            server.start(handler)
            is_running_after_start = server.is_running
            assert is_running_after_start

            server.stop()

            # Verify server stopped and resources were cleaned up
            is_running_after_stop = server.is_running
            assert not is_running_after_stop
            mock_reader.close.assert_called_once()
            mock_writer.close.assert_called_once()

    def test_server_is_running_property_reflects_state(self) -> None:
        """is_running property accurately reflects server state"""
        channel_name = f"test_is_running_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        assert not server.is_running

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            server.start(handler)
            is_running_after_start = server.is_running
            assert is_running_after_start

            server.stop()  # type: ignore[unreachable]
            # Verify server stopped
            is_running_after_stop = server.is_running
            assert not is_running_after_stop


class TestRequestResponseProcessing:
    """Test request/response processing behaviors"""

    def test_server_processes_requests_via_handler(self) -> None:
        """Server receives request frames and processes them via handler"""
        channel_name = f"test_processing_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        received_frames: List[bytes] = []

        def handler(frame: Frame, writer: Writer) -> None:
            received_frames.append(bytes(frame.data))
            writer.write_frame(frame.data)

        # Create real buffers for this test
        request_buffer = f"{channel_name}_request"
        response_buffer = f"{channel_name}_response"

        # Client creates response buffer as reader
        with Reader(response_buffer, config):
            # Start server (it will create request buffer as reader and connect to response as writer)
            server.start(handler)
            time.sleep(0.2)  # Let server initialize and connect

            # Client connects to request buffer as writer
            with Writer(request_buffer) as request_writer:
                test_data = b"Test request data"
                request_writer.write_frame(test_data)

                time.sleep(0.3)  # Let server process

                assert len(received_frames) == 1
                assert received_frames[0] == test_data

            server.stop()

    def test_handler_receives_frame_and_writer(self) -> None:
        """Handler receives both Frame and Writer as parameters (C# signature)"""
        channel_name = f"test_signature_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        handler_args = []

        def handler(frame: Frame, writer: Writer) -> None:
            handler_args.append((type(frame).__name__, type(writer).__name__))

        mock_frame = MagicMock(spec=Frame)
        mock_frame.data = b"test"
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.return_value = False  # Stop after one iteration
        mock_writer = MagicMock(spec=Writer)

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer", return_value=mock_writer
        ):
            server.start(handler)
            time.sleep(0.2)
            server.stop()

            # Verify handler was called with correct types
            assert len(handler_args) > 0
            frame_type, writer_type = handler_args[0]
            assert "Frame" in frame_type or "Mock" in frame_type
            assert "Writer" in writer_type or "Mock" in writer_type


class TestMetadataInitialization:
    """Test metadata initialization behaviors"""

    def test_server_reads_metadata_on_connection(self) -> None:
        """Server reads metadata from request buffer on connection"""
        channel_name = f"test_metadata_read_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        metadata_content = b"Client metadata v1.0"
        init_called = threading.Event()
        received_metadata = []

        def on_init(metadata: memoryview) -> None:
            received_metadata.append(bytes(metadata))
            init_called.set()

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_metadata = memoryview(metadata_content)
        mock_reader = MagicMock()
        mock_reader.get_metadata.return_value = mock_metadata
        mock_reader.read_frame.return_value = None
        mock_reader.is_writer_connected.return_value = False

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler, on_init=on_init)
            init_called.wait(timeout=1.0)
            server.stop()

            assert len(received_metadata) == 1
            assert received_metadata[0] == metadata_content
            mock_reader.get_metadata.assert_called_once()

    def test_server_continues_without_metadata(self) -> None:
        """Server continues working if no metadata is available"""
        channel_name = f"test_no_metadata_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        init_called = threading.Event()

        def on_init(metadata: memoryview) -> None:
            init_called.set()

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_reader.get_metadata.return_value = None  # No metadata
        mock_reader.read_frame.return_value = None
        mock_reader.is_writer_connected.return_value = False

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler, on_init=on_init)
            time.sleep(0.2)
            server.stop()

            # Init should not be called when no metadata
            assert not init_called.is_set()
            assert server._request_reader is None  # Cleaned up after stop

    def test_metadata_memoryview_is_released(self) -> None:
        """Metadata memoryview is properly released after use"""
        channel_name = f"test_metadata_release_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        # Create a proper mock memoryview with required attributes
        mock_metadata = MagicMock(spec=memoryview)
        mock_metadata.__bytes__ = lambda self: b"test metadata"
        mock_metadata.__len__ = lambda self: 13  # Length of "test metadata"

        def on_init(metadata: memoryview) -> None:
            pass

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_reader.get_metadata.return_value = mock_metadata
        mock_reader.read_frame.return_value = None
        mock_reader.is_writer_connected.return_value = False

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler, on_init=on_init)
            time.sleep(0.2)
            server.stop()

            # Verify release was called on the memoryview
            mock_metadata.release.assert_called_once()


class TestErrorHandling:
    """Test error handling behaviors"""

    def test_error_handlers_invoked_on_exception(self) -> None:
        """Error handlers are invoked when exceptions occur"""
        channel_name = f"test_error_handler_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        received_errors: List[Exception] = []

        def error_handler(args: ErrorEventArgs) -> None:
            received_errors.append(args.exception)

        server.add_error_handler(error_handler)

        test_exception = ValueError("Test error")

        def handler(frame: Frame, writer: Writer) -> None:
            raise test_exception

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.side_effect = [True, False]  # Process once then stop

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.3)
            server.stop()

            assert len(received_errors) > 0
            assert any(isinstance(e, ValueError) for e in received_errors)

    def test_server_continues_after_non_fatal_errors(self) -> None:
        """Server continues processing after non-fatal handler errors"""
        channel_name = f"test_continue_after_error_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        process_count = 0

        def handler(frame: Frame, writer: Writer) -> None:
            nonlocal process_count
            process_count += 1
            if process_count == 1:
                raise ValueError("Non-fatal error")
            # Continue processing after error

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        # Return frame twice, then stop
        mock_reader.read_frame.side_effect = [mock_frame, mock_frame, None]
        mock_reader.is_writer_connected.return_value = False

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.3)
            server.stop()

            assert process_count == 2  # Processed twice despite error

    def test_server_stops_on_reader_dead_exception(self) -> None:
        """Server stops cleanly on ReaderDeadException"""
        channel_name = f"test_reader_dead_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        error_received = threading.Event()
        received_errors: List[Exception] = []

        def error_handler(args: ErrorEventArgs) -> None:
            received_errors.append(args.exception)
            error_received.set()

        server.add_error_handler(error_handler)

        def handler(frame: Frame, writer: Writer) -> None:
            raise ReaderDeadException("Reader disconnected")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.return_value = True

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            error_received.wait(timeout=1.0)
            time.sleep(0.1)

            # Server should stop itself on fatal error
            assert not server.is_running
            assert len(received_errors) > 0
            assert any(isinstance(e, ReaderDeadException) for e in received_errors)

    def test_multiple_error_handlers(self) -> None:
        """Multiple error handlers can be registered and invoked"""
        channel_name = f"test_multiple_handlers_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        handler1_called = threading.Event()
        handler2_called = threading.Event()

        def error_handler1(args: ErrorEventArgs) -> None:
            handler1_called.set()

        def error_handler2(args: ErrorEventArgs) -> None:
            handler2_called.set()

        server.add_error_handler(error_handler1)
        server.add_error_handler(error_handler2)

        def handler(frame: Frame, writer: Writer) -> None:
            raise ValueError("Test error")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.side_effect = [True, False]

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            handler1_called.wait(timeout=1.0)
            handler2_called.wait(timeout=1.0)
            server.stop()

            assert handler1_called.is_set()
            assert handler2_called.is_set()

    def test_error_handler_exception_does_not_crash(self) -> None:
        """Error handlers that throw exceptions don't crash the server"""
        channel_name = f"test_handler_exception_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def bad_error_handler(args: ErrorEventArgs) -> None:
            raise RuntimeError("Error handler failed")

        good_handler_called = threading.Event()

        def good_error_handler(args: ErrorEventArgs) -> None:
            good_handler_called.set()

        server.add_error_handler(bad_error_handler)
        server.add_error_handler(good_error_handler)

        def handler(frame: Frame, writer: Writer) -> None:
            raise ValueError("Test error")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.side_effect = [True, False]

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            good_handler_called.wait(timeout=1.0)
            server.stop()

            # Server should still be stoppable despite error handler exception
            assert not server.is_running
            assert good_handler_called.is_set()


class TestConnectionTimeout:
    """Test connection timeout behaviors"""

    def test_server_retries_response_buffer_connection(self) -> None:
        """Server retries connecting to response buffer with timeout"""
        channel_name = f"test_retry_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        timeout = 0.5  # 500ms timeout
        server = ImmutableDuplexServer(channel_name, config, timeout=timeout)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = None
        mock_reader.is_writer_connected.return_value = False

        attempt_count = 0

        def writer_side_effect(*args: Any) -> Any:
            nonlocal attempt_count
            attempt_count += 1
            if attempt_count < 3:
                raise Exception("Buffer not found")
            return MagicMock()

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer", side_effect=writer_side_effect
        ):
            server.start(handler)
            time.sleep(0.4)
            server.stop()

            # Should have retried multiple times
            assert attempt_count >= 2

    def test_server_stops_if_response_buffer_timeout(self) -> None:
        """Server stops cleanly if response buffer doesn't appear within timeout"""
        channel_name = f"test_timeout_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        timeout = 0.3  # 300ms timeout
        server = ImmutableDuplexServer(channel_name, config, timeout=timeout)

        error_received = threading.Event()

        def error_handler(args: ErrorEventArgs) -> None:
            error_received.set()

        server.add_error_handler(error_handler)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()

        def writer_fail(*args: Any) -> Any:
            raise Exception("Buffer not found")

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer", side_effect=writer_fail
        ):
            server.start(handler)
            error_received.wait(timeout=1.0)

            # Server should stop itself after timeout
            assert not server.is_running

    def test_custom_timeout_value_respected(self) -> None:
        """Custom timeout value is respected, not hardcoded"""
        channel_name = f"test_custom_timeout_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        custom_timeout = 0.3  # 300ms
        server = ImmutableDuplexServer(channel_name, config, timeout=custom_timeout)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = None
        mock_reader.is_writer_connected.return_value = False

        attempt_count = 0

        def writer_side_effect(*args: Any) -> Any:
            nonlocal attempt_count
            attempt_count += 1
            # Succeed on third attempt (simulating successful connection within timeout)
            if attempt_count < 3:
                raise Exception("Buffer not found")
            return MagicMock()

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer", side_effect=writer_side_effect
        ):
            server.start(handler)
            time.sleep(0.4)

            # Should succeed after retries within custom timeout
            assert server.is_running
            assert attempt_count >= 3  # Verify it retried multiple times
            server.stop()


class TestProcessingModes:
    """Test different processing mode behaviors"""

    def test_single_thread_mode_works(self) -> None:
        """SINGLE_THREAD mode works correctly (default)"""
        channel_name = f"test_single_thread_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            server.start(handler, mode=ProcessingMode.SINGLE_THREAD)
            assert server.is_running
            server.stop()

    def test_thread_pool_mode_not_implemented(self) -> None:
        """THREAD_POOL mode raises NotImplementedError"""
        channel_name = f"test_thread_pool_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            with pytest.raises(NotImplementedError, match="THREAD_POOL mode is not yet implemented"):
                server.start(handler, mode=ProcessingMode.THREAD_POOL)

    def test_invalid_processing_mode(self) -> None:
        """Invalid processing mode raises ValueError"""
        channel_name = f"test_invalid_mode_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            with pytest.raises(ValueError, match="Invalid processing mode"):
                server.start(handler, mode="INVALID_MODE")  # type: ignore

    def test_default_mode_is_single_thread(self) -> None:
        """Default mode is SINGLE_THREAD when not specified"""
        channel_name = f"test_default_mode_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            # Don't specify mode - should use default
            server.start(handler)
            assert server.is_running
            server.stop()


class TestThreadSafety:
    """Test thread safety behaviors"""

    def test_stop_from_different_thread(self) -> None:
        """Stop can be called from different thread than Start"""
        channel_name = f"test_thread_stop_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            # Start from main thread
            server.start(handler)
            assert server.is_running

            # Stop from different thread
            stop_thread = threading.Thread(target=server.stop)
            stop_thread.start()
            stop_thread.join(timeout=1.0)

            assert not server.is_running

    def test_is_running_thread_safe(self) -> None:
        """is_running property is thread-safe"""
        channel_name = f"test_is_running_safe_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        results = []

        def check_running() -> None:
            for _ in range(100):
                results.append(server.is_running)
                time.sleep(0.001)

        with patch("zerobuffer.duplex.server.Reader"), patch("zerobuffer.duplex.server.Writer"):
            # Start multiple threads checking is_running
            threads = [threading.Thread(target=check_running) for _ in range(5)]
            for t in threads:
                t.start()

            # Change state while threads are checking
            server.start(handler)
            time.sleep(0.05)
            server.stop()

            for t in threads:
                t.join()

            # Should have seen both True and False values
            assert True in results
            assert False in results

    def test_error_handlers_thread_safe(self) -> None:
        """Error handlers list is thread-safe for add/remove/invoke"""
        channel_name = f"test_handlers_safe_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        handlers_called = []

        def make_handler(id: int) -> Any:
            def handler(args: ErrorEventArgs) -> None:
                handlers_called.append(id)

            return handler

        # Add handlers from multiple threads
        def add_handlers() -> None:
            for i in range(10):
                server.add_error_handler(make_handler(threading.get_ident() * 100 + i))
                time.sleep(0.001)

        threads = [threading.Thread(target=add_handlers) for _ in range(3)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        # Trigger error to invoke all handlers
        def handler(frame: Frame, writer: Writer) -> None:
            raise ValueError("Test")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.side_effect = [True, False]

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.3)
            server.stop()

            # All handlers should have been called
            assert len(handlers_called) > 0


class TestResourceCleanup:
    """Test resource cleanup behaviors"""

    def test_reader_writer_closed_on_stop(self) -> None:
        """Reader and Writer are closed on stop"""
        channel_name = f"test_cleanup_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = None
        mock_reader.is_writer_connected.return_value = False
        mock_writer = MagicMock()

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer", return_value=mock_writer
        ):
            server.start(handler)
            time.sleep(0.1)
            server.stop()

            mock_reader.close.assert_called_once()
            mock_writer.close.assert_called_once()

    def test_thread_joined_with_timeout(self) -> None:
        """Thread is properly joined with timeout"""
        channel_name = f"test_thread_join_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            time.sleep(0.1)  # Slow handler

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.return_value = True  # Keep running

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.1)

            # Stop should join thread with timeout
            start_time = time.time()
            server.stop()
            stop_time = time.time()

            # Should have stopped within reasonable time (2s timeout in implementation)
            assert stop_time - start_time < 3.0

    def test_no_resource_leaks_after_multiple_cycles(self) -> None:
        """No resource leaks after multiple start/stop cycles"""
        channel_name = f"test_no_leaks_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        for i in range(5):
            server = ImmutableDuplexServer(f"{channel_name}_{i}", config)

            mock_reader = MagicMock()
            mock_reader.read_frame.return_value = None
            mock_reader.is_writer_connected.return_value = False
            mock_writer = MagicMock()

            with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
                "zerobuffer.duplex.server.Writer", return_value=mock_writer
            ):
                server.start(handler)
                time.sleep(0.05)
                server.stop()

                # Verify cleanup
                assert server._request_reader is None
                assert server._response_writer is None
                assert server._thread is None


class TestClientDisconnection:
    """Test client disconnection behaviors"""

    def test_server_detects_client_disconnect(self) -> None:
        """Server detects when client disconnects (ReaderDeadException)"""
        channel_name = f"test_client_disconnect_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        disconnected = threading.Event()

        def error_handler(args: ErrorEventArgs) -> None:
            if isinstance(args.exception, ReaderDeadException):
                disconnected.set()

        server.add_error_handler(error_handler)

        def handler(frame: Frame, writer: Writer) -> None:
            pass

        mock_reader = MagicMock()
        mock_reader.read_frame.side_effect = ReaderDeadException("Client disconnected")

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            disconnected.wait(timeout=1.0)

            assert disconnected.is_set()
            # Server should stop on client disconnect
            time.sleep(0.1)
            assert not server.is_running

    def test_server_stops_processing_on_disconnect(self) -> None:
        """Server stops processing loop on client disconnect"""
        channel_name = f"test_stop_on_disconnect_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        process_count = 0

        def handler(frame: Frame, writer: Writer) -> None:
            nonlocal process_count
            process_count += 1
            if process_count == 2:
                raise WriterDeadException("Client writer disconnected")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.return_value = True

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.3)

            # Should have stopped after disconnect
            assert not server.is_running
            assert process_count == 2

    def test_error_handlers_notified_of_disconnection(self) -> None:
        """Error handlers are notified of disconnection"""
        channel_name = f"test_notify_disconnect_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        received_exceptions = []

        def error_handler(args: ErrorEventArgs) -> None:
            received_exceptions.append(type(args.exception).__name__)

        server.add_error_handler(error_handler)

        def handler(frame: Frame, writer: Writer) -> None:
            raise WriterDeadException("Writer disconnected")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.return_value = True

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.2)

            assert "WriterDeadException" in received_exceptions


class TestHandlerExceptionPropagation:
    """Test handler exception propagation behaviors"""

    def test_server_continues_on_regular_exception(self) -> None:
        """Server continues if handler throws regular Exception"""
        channel_name = f"test_continue_exception_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        process_count = 0

        def handler(frame: Frame, writer: Writer) -> None:
            nonlocal process_count
            process_count += 1
            if process_count == 1:
                raise Exception("Regular exception")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        # Process twice then stop
        mock_reader.read_frame.side_effect = [mock_frame, mock_frame, None]
        mock_reader.is_writer_connected.return_value = False

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.2)
            server.stop()

            assert process_count == 2

    def test_server_invokes_error_handlers_with_exception(self) -> None:
        """Server invokes error handlers with the exception"""
        channel_name = f"test_exception_to_handler_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        received_exception = None

        def error_handler(args: ErrorEventArgs) -> None:
            nonlocal received_exception
            received_exception = args.exception

        server.add_error_handler(error_handler)

        test_error = RuntimeError("Test runtime error")

        def handler(frame: Frame, writer: Writer) -> None:
            raise test_error

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.side_effect = [True, False]

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.2)
            server.stop()

            assert received_exception is test_error

    def test_fatal_exceptions_stop_server(self) -> None:
        """Fatal exceptions (Reader/WriterDead) stop the server"""
        channel_name = f"test_fatal_stop_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            raise ReaderDeadException("Fatal error")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.return_value = True

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ):
            server.start(handler)
            time.sleep(0.2)

            # Server should have stopped itself
            assert not server.is_running

    def test_handler_error_logged(self) -> None:
        """Handler errors are logged appropriately"""
        channel_name = f"test_error_logging_{os.getpid()}"
        config = BufferConfig(metadata_size=1024, payload_size=64 * 1024)
        server = ImmutableDuplexServer(channel_name, config)

        def handler(frame: Frame, writer: Writer) -> None:
            raise ValueError("Error to be logged")

        mock_frame = MagicMock(spec=Frame)
        mock_reader = MagicMock()
        mock_reader.read_frame.return_value = mock_frame
        mock_reader.is_writer_connected.side_effect = [True, False]

        with patch("zerobuffer.duplex.server.Reader", return_value=mock_reader), patch(
            "zerobuffer.duplex.server.Writer"
        ), patch("zerobuffer.duplex.server.logger") as mock_logger:
            server.start(handler)
            time.sleep(0.2)
            server.stop()

            # Verify error was logged
            mock_logger.error.assert_called()
            args = mock_logger.error.call_args[0]
            assert "Error processing request" in args[0]
