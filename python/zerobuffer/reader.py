"""
ZeroBuffer Reader implementation

Provides zero-copy reading from shared memory buffers.
"""

import os
import threading
from typing import Optional, Any
from pathlib import Path
import logging

from . import platform
from .types import BufferConfig, Frame, FrameHeader, align_to_boundary
from .oieb_view import OIEBView
from .exceptions import ZeroBufferException, WriterDeadException, SequenceError
from .shared_memory import SharedMemoryFactory

# Module logger
logger = logging.getLogger(__name__)


class Reader:
    """
    Zero-copy reader for ZeroBuffer

    The Reader creates and owns the shared memory buffer. It waits for a Writer
    to connect and then reads frames with zero-copy access.
    """

    def __init__(self, name: str, config: Optional[BufferConfig] = None):
        """
        Create a new ZeroBuffer reader

        Args:
            name: Name of the buffer
            config: Buffer configuration (uses defaults if not provided)
        """
        self.name = name
        self._config = config or BufferConfig()
        self._lock = threading.RLock()
        self._closed = False
        self._expected_sequence = 1
        self._frames_read = 0
        self._bytes_read = 0
        self._current_frame_size = 0
        self._frame_disposal_data: Optional[tuple[int, int]] = (
            None  # Will store (total_frame_size, sequence) for cached callback
        )
        self._oieb: Optional[OIEBView] = None  # Will be initialized after shm is created

        logger.debug("Creating Reader with name=%s, config=%s", name, self._config)

        # Calculate aligned sizes
        self._oieb_size = align_to_boundary(OIEBView.SIZE)
        self._metadata_size = align_to_boundary(self._config.metadata_size)
        self._payload_size = align_to_boundary(self._config.payload_size)

        total_size = self._oieb_size + self._metadata_size + self._payload_size

        # Clean up stale resources
        logger.debug("Cleaning up stale resources before creating buffer")
        self._cleanup_stale_resources()

        # Create lock file
        lock_path = Path(platform.get_temp_directory()) / f"{name}.lock"
        self._lock_file = platform.create_file_lock(str(lock_path))

        try:
            # Create shared memory using the new abstraction
            logger.info("Creating shared memory: name=%s, size=%d bytes", name, total_size)
            self._shm = SharedMemoryFactory.create(name, total_size)

            # Don't create persistent memoryviews - use the API directly
            # This avoids the "cannot close exported pointers" error

            # Initialize OIEB using direct memory view
            self._oieb = OIEBView(self._shm.get_memoryview(0, OIEBView.SIZE))
            self._oieb.initialize(
                metadata_size=self._metadata_size, payload_size=self._payload_size, reader_pid=os.getpid()
            )

            logger.debug(
                "Initialized OIEB: payload_free_bytes=%d, payload_size=%d",
                self._oieb.payload_free_bytes,
                self._oieb.payload_size,
            )

            # Create semaphores
            logger.debug("Creating semaphores: write=%s, read=%s", f"sem-w-{name}", f"sem-r-{name}")
            self._sem_write = platform.create_semaphore(f"sem-w-{name}", 0)
            self._sem_read = platform.create_semaphore(f"sem-r-{name}", 0)

            logger.info("Reader created successfully: pid=%d", os.getpid())

        except Exception as e:
            logger.error("Failed to create Reader: %s", e)
            self._cleanup_on_error()
            raise

    def _cleanup_stale_resources(self) -> None:
        """Clean up stale resources from dead processes"""
        lock_dir = Path(platform.get_temp_directory())

        try:
            lock_dir.mkdir(parents=True, exist_ok=True)

            for lock_file in lock_dir.glob("*.lock"):
                # Use the platform-specific file lock implementation
                if hasattr(platform, "PlatformFileLock"):
                    try_remove = platform.PlatformFileLock.try_remove_stale
                else:
                    # Fallback to Linux implementation if available
                    linux_file_lock = getattr(platform, "LinuxFileLock", None)
                    if linux_file_lock:
                        try_remove = getattr(linux_file_lock, "try_remove_stale", lambda path: False)
                    else:

                        def try_remove(path: str) -> bool:  # noqa: E731
                            return False

                if try_remove(str(lock_file)):
                    # We removed a stale lock, clean up associated resources
                    buffer_name = lock_file.stem
                    logger.debug("Found stale lock file for buffer: %s", buffer_name)

                    try:
                        # Check if shared memory exists and is orphaned
                        shm = SharedMemoryFactory.open(buffer_name)
                        # Use OIEBView to check PIDs
                        temp_oieb = OIEBView(shm.get_memoryview(0, OIEBView.SIZE))

                        # Check if both reader and writer are dead
                        reader_dead = temp_oieb.reader_pid == 0 or not platform.process_exists(temp_oieb.reader_pid)
                        writer_dead = temp_oieb.writer_pid == 0 or not platform.process_exists(temp_oieb.writer_pid)

                        if reader_dead and writer_dead:
                            # Both processes are dead, safe to clean up
                            logger.info(
                                "Cleaning up orphaned buffer: %s (reader_pid=%d, writer_pid=%d)",
                                buffer_name,
                                temp_oieb.reader_pid,
                                temp_oieb.writer_pid,
                            )
                            shm.close()
                            shm.unlink()

                            # Clean up semaphores
                            try:
                                sem = platform.open_semaphore(f"sem-w-{buffer_name}")
                                sem.close()
                                sem.unlink()
                            except Exception:
                                pass

                            try:
                                sem = platform.open_semaphore(f"sem-r-{buffer_name}")
                                sem.close()
                                sem.unlink()
                            except Exception:
                                pass
                    except Exception:
                        # If we can't open shared memory, clean up anyway
                        pass
        except Exception:
            # Ignore errors during cleanup
            pass

    def _cleanup_on_error(self) -> None:
        """Clean up resources on initialization error"""
        # Dispose OIEBView first to release memoryview
        if hasattr(self, "_oieb") and self._oieb:
            self._oieb.dispose()
            self._oieb = None
        if hasattr(self, "_sem_read"):
            self._sem_read.close()
            self._sem_read.unlink()
        if hasattr(self, "_sem_write"):
            self._sem_write.close()
            self._sem_write.unlink()
        if hasattr(self, "_shm"):
            self._shm.close()
            self._shm.unlink()
        if hasattr(self, "_lock_file"):
            self._lock_file.close()

    # OIEB access is now direct through self._oieb (OIEBView)
    # No need for _read_oieb or _write_oieb methods

    def _on_frame_disposed(self) -> None:
        """
        Cached callback for frame disposal - avoids per-frame allocations.
        This method is called when a Frame is disposed (via with statement or explicit dispose).
        The frame data is stored in _frame_disposal_data before creating the Frame.
        """
        if self._frame_disposal_data is None:
            return

        total_frame_size, sequence = self._frame_disposal_data
        self._frame_disposal_data = None  # Clear for next frame

        # Check if the reader is still open before trying to release semaphore
        if self._closed:
            return

        logger.debug("Frame disposed, releasing %d bytes for seq=%d", total_frame_size, sequence)
        # Update OIEB directly in shared memory
        if self._oieb:
            self._oieb.payload_free_bytes += total_frame_size
            # Flush after updating payload_free_bytes (matching C# line 515 in OnFrameDisposed)
            if self._shm:
                self._shm.flush()
        # Signal writer that space is available
        # Only release if semaphore is still valid
        if hasattr(self, "_sem_read") and self._sem_read:
            try:
                self._sem_read.release()
            except Exception:
                # Semaphore might be closed, ignore
                pass

    def _calculate_used_bytes(self, write_pos: int, read_pos: int, buffer_size: int) -> int:
        """Calculate used bytes in circular buffer"""
        if write_pos >= read_pos:
            return write_pos - read_pos
        else:
            return buffer_size - read_pos + write_pos

    def get_metadata(self) -> Optional[memoryview]:
        """
        Get metadata as zero-copy memoryview

        Returns:
            Memoryview of metadata or None if no metadata written
        """
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Reader is closed")

            if not self._oieb or self._oieb.metadata_written_bytes == 0:
                return None

            # Read metadata size prefix
            metadata_offset = self._oieb_size
            size_bytes = self._shm.read_bytes(metadata_offset, 8)
            size = int.from_bytes(size_bytes, byteorder="little")

            if size == 0 or size > self._oieb.metadata_written_bytes - 8:
                raise ZeroBufferException("Invalid metadata size")

            # Return view of actual metadata (skip size prefix)
            return self._shm.get_memoryview(metadata_offset + 8, size)

    def read_frame(self, timeout: Optional[float] = 5.0) -> Optional[Frame]:
        """
        Read next frame from buffer (zero-copy)

        Args:
            timeout: Timeout in seconds, None for infinite

        Returns:
            Frame object or None if timeout

        Raises:
            WriterDeadException: If writer process died
            SequenceError: If sequence number is invalid
        """
        logger.debug("ReadFrame called with timeout=%s", timeout)

        with self._lock:
            if self._closed:
                raise ZeroBufferException("Reader is closed")

            while True:
                # Wait for data signal FIRST (following the protocol correctly)
                logger.debug("Waiting on write semaphore for data signal")
                if not self._sem_write.acquire(timeout):
                    # Timeout - check if writer is alive or disconnected gracefully
                    if self._oieb:
                        if self._oieb.writer_pid == 0:
                            # Writer disconnected gracefully - check if all frames have been read
                            if self._oieb.payload_written_count <= self._oieb.payload_read_count:
                                logger.info("Writer disconnected gracefully, all frames read")
                                raise WriterDeadException()
                        elif not platform.process_exists(self._oieb.writer_pid):
                            # Writer process died unexpectedly
                            logger.warning("Writer process %d is dead", self._oieb.writer_pid)
                            raise WriterDeadException()
                    logger.debug("Read timeout after %s seconds", timeout)
                    return None  # Timeout

                # Semaphore was signaled - data should be available

                # Quick check to ensure writer hasn't disconnected gracefully
                # When writer_pid == 0 we can check payload_written_count as it won't be changed anymore by external process
                if (
                    self._oieb
                    and self._oieb.writer_pid == 0
                    and self._oieb.payload_written_count <= self._oieb.payload_read_count
                ):
                    raise WriterDeadException()

                if self._oieb:
                    logger.debug(
                        "OIEB state after semaphore: WrittenCount=%d, ReadCount=%d, WritePos=%d, ReadPos=%d, FreeBytes=%d, PayloadSize=%d",
                        self._oieb.payload_written_count,
                        self._oieb.payload_read_count,
                        self._oieb.payload_write_pos,
                        self._oieb.payload_read_pos,
                        self._oieb.payload_free_bytes,
                        self._oieb.payload_size,
                    )

                # Read frame header
                if not self._oieb:
                    raise ZeroBufferException("Reader not properly initialized")

                payload_base = self._oieb_size + self._metadata_size
                header_offset = payload_base + self._oieb.payload_read_pos
                header_data = self._shm.read_bytes(header_offset, FrameHeader.SIZE)
                header = FrameHeader.unpack(header_data)

                # Check for wrap-around marker
                if header.payload_size == 0:
                    # This is a wrap marker
                    logger.debug("Found wrap marker at position %d, handling wrap-around", self._oieb.payload_read_pos)

                    # Calculate wasted space from current read position to end of buffer
                    wasted_space = self._oieb.payload_size - self._oieb.payload_read_pos
                    logger.debug(
                        "Wrap-around: wasted space = %d bytes (from %d to %d)",
                        wasted_space,
                        self._oieb.payload_read_pos,
                        self._oieb.payload_size,
                    )

                    # Add back the wasted space to free bytes
                    self._oieb.payload_free_bytes += wasted_space

                    # Move to beginning of buffer
                    self._oieb.payload_read_pos = 0
                    self._oieb.payload_read_count += 1  # Count the wrap marker as a "frame"

                    logger.debug(
                        "After wrap: ReadPos=0, ReadCount=%d, FreeBytes=%d",
                        self._oieb.payload_read_count,
                        self._oieb.payload_free_bytes,
                    )

                    # Signal that we consumed the wrap marker (freed space)
                    self._sem_read.release()

                    # Now read the actual frame at the beginning without waiting for another semaphore
                    # The writer wrote both the wrap marker and the frame before signaling
                    header_offset = payload_base  # Start of buffer
                    header_data = self._shm.read_bytes(header_offset, FrameHeader.SIZE)
                    header = FrameHeader.unpack(header_data)

                # Validate sequence number
                if header.sequence_number != self._expected_sequence:
                    logger.error("Sequence error: expected %d, got %d", self._expected_sequence, header.sequence_number)
                    raise SequenceError(self._expected_sequence, header.sequence_number)

                # Validate frame size
                if header.payload_size == 0:
                    logger.error("Invalid frame size: 0")
                    raise ZeroBufferException("Invalid frame size: 0")

                total_frame_size = FrameHeader.SIZE + header.payload_size

                logger.debug(
                    "Reading frame: seq=%d, size=%d from position %d",
                    header.sequence_number,
                    header.payload_size,
                    self._oieb.payload_read_pos,
                )

                # Check if frame wraps around buffer
                if self._oieb.payload_read_pos + total_frame_size > self._oieb.payload_size:
                    # Frame would extend beyond buffer
                    if self._oieb.payload_write_pos < self._oieb.payload_read_pos:
                        # Writer has wrapped, we should wrap too
                        self._oieb.payload_read_pos = 0
                        # Flush after wrap-around handling (matching C# line 369)
                        self._shm.flush()
                        # Re-read header at new position
                        header_offset = payload_base  # Start of payload buffer
                        header_data = self._shm.read_bytes(header_offset, FrameHeader.SIZE)
                        header = FrameHeader.unpack(header_data)

                        # Re-validate sequence number after wrap
                        if header.sequence_number != self._expected_sequence:
                            raise SequenceError(self._expected_sequence, header.sequence_number)
                    else:
                        # Writer hasn't wrapped yet, wait
                        continue

                # Update OIEB read position and count (but NOT free bytes yet!)
                old_pos = self._oieb.payload_read_pos
                self._oieb.payload_read_pos += total_frame_size
                if self._oieb.payload_read_pos >= self._oieb.payload_size:
                    self._oieb.payload_read_pos -= self._oieb.payload_size
                self._oieb.payload_read_count += 1
                # NOTE: We do NOT update payload_free_bytes here!
                # This will be done when the Frame is disposed (RAII pattern)

                logger.debug(
                    "Frame read: seq=%d, new state: ReadCount=%d, ReadPos=%d",
                    header.sequence_number,
                    self._oieb.payload_read_count,
                    self._oieb.payload_read_pos,
                )

                # Flush after reading frame and updating OIEB (matching C# line 408)
                self._shm.flush()

                # Store frame disposal data for the cached callback
                # This avoids creating a new lambda/closure for each frame
                self._frame_disposal_data = (total_frame_size, header.sequence_number)

                # Create frame reference (zero-copy) with cached disposal callback
                # Get a fresh memoryview for the payload area
                payload_view = self._shm.get_memoryview(payload_base, self._payload_size)
                frame = Frame(
                    memory_view=payload_view,
                    offset=old_pos + FrameHeader.SIZE,  # Use old_pos since we already updated
                    size=header.payload_size,
                    sequence=header.sequence_number,
                    on_dispose=self._on_frame_disposed,  # Use cached method - no allocation!
                )

                # Update tracking
                self._current_frame_size = total_frame_size
                self._expected_sequence += 1
                self._frames_read += 1
                self._bytes_read += header.payload_size

                return frame

    def release_frame(self, frame: Frame) -> None:
        """
        Release frame and free buffer space

        This triggers the frame disposal which updates OIEB and signals the writer.

        Args:
            frame: Frame to release
        """
        # Dispose the frame to trigger the RAII cleanup
        frame.dispose()

    def is_writer_connected(self, timeout_ms: Optional[int] = None) -> bool:
        """
        Check if a writer is connected to the buffer

        Args:
            timeout_ms: Optional timeout in milliseconds to wait for writer connection.
                       If None, checks immediately and returns.
                       If specified, waits up to timeout_ms for a writer to connect.

        Returns:
            True if writer is connected, False otherwise
        """
        import time

        with self._lock:
            if self._closed:
                return False

            if timeout_ms is None:
                # Immediate check
                if not self._oieb:
                    return False
                return self._oieb.writer_pid != 0 and platform.process_exists(self._oieb.writer_pid)

            # Wait for writer connection with timeout
            start_time = time.time() * 1000  # Convert to milliseconds
            end_time = start_time + timeout_ms

            while True:
                # Debug: Check raw bytes using the clean API
                raw_bytes = self._shm.read_bytes(0, 16)
                logger.debug("Raw first 16 bytes: %s", raw_bytes.hex())
                # Check writer_pid bytes at offset 80
                writer_pid_bytes = self._shm.read_bytes(80, 8)
                logger.debug("Raw bytes 80-88 (writer_pid): %s", writer_pid_bytes.hex())

                if self._oieb:
                    logger.debug("Checking writer connection: writer_pid=%d", self._oieb.writer_pid)
                    if self._oieb.writer_pid != 0 and platform.process_exists(self._oieb.writer_pid):
                        logger.debug("Writer connected! PID=%d", self._oieb.writer_pid)
                        return True

                current_time = time.time() * 1000
                if current_time >= end_time:
                    logger.debug("Timeout waiting for writer")
                    return False

                # Sleep for a short time before checking again
                remaining = end_time - current_time
                sleep_time = min(100, remaining) / 1000.0  # Sleep up to 100ms
                if sleep_time > 0:
                    time.sleep(sleep_time)

    @property
    def frames_read(self) -> int:
        """Get number of frames read"""
        return self._frames_read

    @property
    def bytes_read(self) -> int:
        """Get number of bytes read"""
        return self._bytes_read

    def close(self) -> None:
        """Close the reader and clean up resources"""
        with self._lock:
            if self._closed:
                return

            logger.info("Closing Reader: frames_read=%d, bytes_read=%d", self._frames_read, self._bytes_read)

            self._closed = True

            # Clear reader PID
            try:
                if self._oieb:
                    self._oieb.reader_pid = 0
                    # Flush after clearing reader_pid (matching C# line 535 in Dispose)
                    if self._shm:
                        self._shm.flush()
            except Exception:
                pass

            # Properly dispose the OIEBView to release its memoryview
            if self._oieb:
                self._oieb.dispose()
                self._oieb = None

            # Close and unlink resources (reader owns them)
            if hasattr(self, "_sem_read"):
                self._sem_read.close()
                self._sem_read.unlink()

            if hasattr(self, "_sem_write"):
                self._sem_write.close()
                self._sem_write.unlink()

            if hasattr(self, "_shm"):
                self._shm.close()
                self._shm.unlink()

            if hasattr(self, "_lock_file"):
                self._lock_file.close()

    def __enter__(self) -> "Reader":
        return self

    def __exit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        self.close()

    def __del__(self) -> None:
        self.close()
