"""
ZeroBuffer Writer implementation

Provides zero-copy writing to shared memory buffers.
"""

import os
import threading
from datetime import timedelta
from typing import Optional, Union, Any
import logging

from . import platform
from .types import FrameHeader, align_to_boundary
from .oieb_view import OIEBView
from .exceptions import (
    ZeroBufferException,
    ReaderDeadException,
    WriterAlreadyConnectedException,
    BufferFullException,
    FrameTooLargeException,
    InvalidFrameSizeException,
    MetadataAlreadyWrittenException,
)
from .shared_memory import SharedMemoryFactory

# Module logger
logger = logging.getLogger(__name__)


class Writer:
    """
    Zero-copy writer for ZeroBuffer

    The Writer connects to an existing buffer created by a Reader and writes
    frames with zero-copy operations when possible.
    """

    def __init__(self, name: str):
        """
        Connect to an existing ZeroBuffer

        Args:
            name: Name of the buffer to connect to

        Raises:
            ZeroBufferException: If buffer doesn't exist
            WriterAlreadyConnectedException: If another writer is connected
        """
        # Initialize lock first to ensure it exists even if init fails
        self._lock = threading.RLock()
        self.name = name
        self._closed = False
        self._sequence_number = 1
        self._frames_written = 0
        self._bytes_written = 0
        self._metadata_written = False
        self._write_timeout = timedelta(seconds=5)  # Default timeout
        self._oieb: Optional[OIEBView] = None  # Will be initialized after shm is opened

        logger.debug("Creating Writer for buffer: %s", name)

        try:
            # Open existing shared memory using the new abstraction
            logger.debug("Opening shared memory: %s", name)
            self._shm = SharedMemoryFactory.open(name)
            logger.debug("Shared memory opened successfully, size: %d", self._shm.size)

            # Initialize OIEB view for direct memory access
            logger.debug("Creating OIEB view")
            self._oieb = OIEBView(self._shm.get_memoryview(0, OIEBView.SIZE))

            # Verify OIEB
            logger.debug("OIEB: size=%d, version=%d.%d.%d, reader_pid=%d, writer_pid=%d", 
                        self._oieb.oieb_size, 
                        self._oieb.version.major, self._oieb.version.minor, self._oieb.version.patch,
                        self._oieb.reader_pid, self._oieb.writer_pid)
            if self._oieb.oieb_size != 128:
                raise ZeroBufferException(f"Invalid OIEB size: {self._oieb.oieb_size} - version mismatch?")

            # Check if reader exists
            if self._oieb.reader_pid == 0:
                raise ZeroBufferException("No reader PID set in OIEB")
            else:
                logger.debug("Reader PID from OIEB: %d (not verifying during init)", self._oieb.reader_pid)

            # Check if another writer exists
            if self._oieb.writer_pid != 0 and platform.process_exists(self._oieb.writer_pid):
                raise WriterAlreadyConnectedException()

            # Set writer PID directly in shared memory (OIEBView updates immediately)
            self._oieb.writer_pid = os.getpid()
            logger.debug("Set writer PID=%d directly in OIEB shared memory", self._oieb.writer_pid)
            logger.debug("OIEB state: writer_pid=%d, reader_pid=%d", self._oieb.writer_pid, self._oieb.reader_pid)

            # Store layout info
            self._oieb_size = align_to_boundary(self._oieb.oieb_size)
            self._metadata_size = self._oieb.metadata_size
            self._payload_size = self._oieb.payload_size

            # Open semaphores
            self._sem_write = platform.open_semaphore(f"sem-w-{name}")
            self._sem_read = platform.open_semaphore(f"sem-r-{name}")

            # Check if metadata was already written
            self._metadata_written = self._oieb.metadata_written_bytes > 0

        except FileNotFoundError:
            # No shared memory found - reader must be created first
            self._cleanup_on_error()
            raise ZeroBufferException(f"No shared memory found for buffer '{name}' - reader must be created first")
        except Exception:
            self._cleanup_on_error()
            raise

    def _cleanup_on_error(self) -> None:
        """Clean up resources on initialization error"""
        # Dispose OIEBView first to release memoryview
        if hasattr(self, "_oieb") and self._oieb:
            self._oieb.dispose()
            self._oieb = None
        if hasattr(self, "_sem_read"):
            self._sem_read.close()
        if hasattr(self, "_sem_write"):
            self._sem_write.close()
        if hasattr(self, "_shm"):
            self._shm.close()

    # OIEB access is now direct through self._oieb (OIEBView)
    # No need for _read_oieb or _write_oieb methods

    def set_metadata(self, data: Union[bytes, bytearray, memoryview]) -> None:
        """
        Set metadata (can only be called once)

        Args:
            data: Metadata to write

        Raises:
            MetadataAlreadyWrittenException: If metadata was already written
            ZeroBufferException: If metadata is too large
        """
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Writer is closed")

            if self._metadata_written:
                raise MetadataAlreadyWrittenException()

            if not self._oieb:
                raise ZeroBufferException("Writer not properly initialized")

            # Check size with header
            total_size = 8 + len(data)  # 8 bytes for size prefix
            if total_size > self._oieb.metadata_size:
                raise ZeroBufferException("Metadata too large for buffer")

            # Write size prefix
            metadata_offset = self._oieb_size
            self._shm.write_uint64(metadata_offset, len(data))

            # Write metadata
            if len(data) > 0:
                # Convert to bytes if needed
                if isinstance(data, memoryview):
                    self._shm.write_bytes(metadata_offset + 8, bytes(data))
                elif isinstance(data, bytearray):
                    self._shm.write_bytes(metadata_offset + 8, bytes(data))
                else:
                    self._shm.write_bytes(metadata_offset + 8, data)

            # Update OIEB
            self._oieb.metadata_written_bytes = total_size
            self._oieb.metadata_free_bytes = self._oieb.metadata_size - total_size

            logger.info("Updating OIEB after metadata write:")
            logger.info("  metadata_written_bytes: %d", self._oieb.metadata_written_bytes)
            logger.info("  metadata_free_bytes: %d", self._oieb.metadata_free_bytes)
            logger.info("  payload_free_bytes (unchanged): %d", self._oieb.payload_free_bytes)

            # Flush shared memory to ensure metadata is visible
            self._shm.flush()

            # Verify the write directly from shared memory
            logger.info("Verified OIEB after metadata write:")
            logger.info("  metadata_written_bytes: %d", self._oieb.metadata_written_bytes)
            logger.info("  payload_free_bytes: %d", self._oieb.payload_free_bytes)

            self._metadata_written = True

    def _calculate_used_bytes(self, write_pos: int, read_pos: int, buffer_size: int) -> int:
        """Calculate used bytes in circular buffer"""
        if write_pos >= read_pos:
            return write_pos - read_pos
        else:
            return buffer_size - read_pos + write_pos

    def _get_continuous_free_space(self) -> int:
        """Calculate continuous free space in buffer"""
        if not self._oieb:
            return 0
        if self._oieb.payload_write_pos >= self._oieb.payload_read_pos:
            # Write ahead of read - check space to end and beginning
            space_to_end = self._oieb.payload_size - self._oieb.payload_write_pos
            if self._oieb.payload_read_pos == 0:
                # Can't wrap if reader at beginning
                return space_to_end
            # Can use space at beginning if we wrap
            return max(space_to_end, self._oieb.payload_read_pos)
        else:
            # Read ahead of write - continuous space until read pos
            return self._oieb.payload_read_pos - self._oieb.payload_write_pos

    def write_frame(self, data: Union[bytes, bytearray, memoryview]) -> None:
        """
        Write a frame to the buffer

        This method copies the data into the shared memory buffer.
        When passed a memoryview, this is a true zero-copy operation.

        Args:
            data: Frame data to write

        Raises:
            InvalidFrameSizeException: If data is empty
            FrameTooLargeException: If frame is too large for buffer
            ReaderDeadException: If reader process died
        """
        if len(data) == 0:
            raise InvalidFrameSizeException()

        logger.debug("WriteFrame called with data size=%d", len(data))

        with self._lock:
            if self._closed:
                raise ZeroBufferException("Writer is closed")

            if not self._oieb:
                raise ZeroBufferException("Writer not properly initialized")

            frame_size = len(data)
            total_size = FrameHeader.SIZE + frame_size

            # Early check if reader has disconnected gracefully
            if self._oieb.reader_pid == 0:
                raise ReaderDeadException()

            # Check if frame is too large
            if total_size > self._oieb.payload_size:
                raise FrameTooLargeException()

            while True:
                # Check if reader hasn't exited gracefully
                if self._oieb.reader_pid == 0:
                    raise ReaderDeadException()

                logger.debug(
                    "Write frame check: total_size=%d, payload_free_bytes=%d, payload_size=%d",
                    total_size,
                    self._oieb.payload_free_bytes,
                    self._oieb.payload_size,
                )

                # Check for available space
                if self._oieb.payload_free_bytes >= total_size:
                    # We have enough space
                    logger.debug("Have enough space, breaking from wait loop")
                    break

                # Log detailed state before waiting
                logger.info("About to wait on semaphore - not enough space")
                logger.info("  total_size needed: %d", total_size)
                logger.info("  payload_free_bytes: %d", self._oieb.payload_free_bytes)
                logger.info("  payload_size: %d", self._oieb.payload_size)
                logger.info("  payload_write_pos: %d", self._oieb.payload_write_pos)
                logger.info("  payload_read_pos: %d", self._oieb.payload_read_pos)
                logger.info("  metadata_written_bytes: %d", self._oieb.metadata_written_bytes)
                logger.info("  reader_pid: %d", self._oieb.reader_pid)
                logger.info("  writer_pid: %d", self._oieb.writer_pid)

                # Log current OIEB state (direct from shared memory)
                logger.debug("Current OIEB state: free_bytes=%d", self._oieb.payload_free_bytes)

                # Wait for reader to free space (blocking)
                logger.info("Waiting on sem_read semaphore...")
                if not self._sem_read.acquire(timeout=self._write_timeout.total_seconds()):
                    # Timeout - check if reader is alive
                    if self._oieb.reader_pid == 0 or not platform.process_exists(self._oieb.reader_pid):
                        raise ReaderDeadException()
                    # Buffer is full - throw exception like C# does
                    raise BufferFullException()

                # Re-read OIEB after semaphore for next iteration

            # Check if we need to wrap
            continuous_free = self._get_continuous_free_space()
            space_to_end = self._oieb.payload_size - self._oieb.payload_write_pos

            # We need to wrap if frame doesn't fit in continuous space
            if continuous_free >= total_size and space_to_end < total_size and self._oieb.payload_read_pos > 0:
                # Need to wrap to beginning
                # Write a special marker if there's space for at least a header
                if space_to_end >= FrameHeader.SIZE:
                    # Write wrap marker header
                    wrap_header = FrameHeader(payload_size=0, sequence_number=0)
                    payload_base = self._oieb_size + self._metadata_size
                    wrap_offset = payload_base + self._oieb.payload_write_pos
                    self._shm.write_bytes(wrap_offset, wrap_header.pack())

                # Account for the wasted space at the end
                self._oieb.payload_free_bytes -= space_to_end

                # Move to beginning of buffer
                self._oieb.payload_write_pos = 0
                self._oieb.payload_written_count += 1  # Count the wrap marker

            # Write frame header
            header = FrameHeader(payload_size=frame_size, sequence_number=self._sequence_number)
            payload_base = self._oieb_size + self._metadata_size
            header_offset = payload_base + self._oieb.payload_write_pos
            self._shm.write_bytes(header_offset, header.pack())

            # Write frame data
            data_offset = header_offset + FrameHeader.SIZE
            self._shm.write_bytes(data_offset, bytes(data))

            # Update tracking
            self._oieb.payload_write_pos += total_size
            self._sequence_number += 1
            self._frames_written += 1
            self._bytes_written += frame_size

            # Update OIEB
            self._oieb.payload_free_bytes -= total_size
            self._oieb.payload_written_count += 1

            # Flush shared memory to ensure all writes are visible
            self._shm.flush()

            # Signal reader
            self._sem_write.release()

    def get_frame_buffer(self, size: int) -> memoryview:
        """
        Get a buffer for direct writing (advanced zero-copy API)

        This method returns a memoryview where you can write data directly.
        You must call commit_frame() after writing to complete the operation.

        Args:
            size: Size of frame data to write

        Returns:
            Memoryview for writing frame data

        Raises:
            InvalidFrameSizeException: If size is zero
            FrameTooLargeException: If frame is too large for buffer
            ReaderDeadException: If reader process died
        """
        if size == 0:
            raise InvalidFrameSizeException()

        # Acquire lock - will be released in commit_frame
        self._lock.acquire()
        try:
            if self._closed:
                self._lock.release()
                raise ZeroBufferException("Writer is closed")

            if not self._oieb:
                self._lock.release()
                raise ZeroBufferException("Writer not properly initialized")

            total_size = FrameHeader.SIZE + size

            # Early check if reader has disconnected gracefully
            if self._oieb.reader_pid == 0:
                raise ReaderDeadException()

            if total_size > self._oieb.payload_size:
                raise FrameTooLargeException()

            # Wait for space (same logic as write_frame)
            while True:
                # Check if reader hasn't exited gracefully
                if self._oieb.reader_pid == 0:
                    raise ReaderDeadException()

                # Check for available space
                if self._oieb.payload_free_bytes >= total_size:
                    break

                if not self._sem_read.acquire(timeout=self._write_timeout.total_seconds()):
                    if self._oieb.reader_pid == 0 or not platform.process_exists(self._oieb.reader_pid):
                        raise ReaderDeadException()
                    # Buffer is full - throw exception like C# does
                    raise BufferFullException()

                # Re-read OIEB after semaphore for next iteration

            # Handle wrap-around if needed
            continuous_free = self._get_continuous_free_space()
            space_to_end = self._oieb.payload_size - self._oieb.payload_write_pos

            # We need to wrap if frame doesn't fit in continuous space
            if continuous_free >= total_size and space_to_end < total_size and self._oieb.payload_read_pos > 0:
                if space_to_end >= FrameHeader.SIZE:
                    # Write wrap marker header
                    wrap_header = FrameHeader(payload_size=0, sequence_number=0)
                    payload_base = self._oieb_size + self._metadata_size
                    wrap_offset = payload_base + self._oieb.payload_write_pos
                    self._shm.write_bytes(wrap_offset, wrap_header.pack())

                # Account for the wasted space at the end
                self._oieb.payload_free_bytes -= space_to_end

                # Move to beginning of buffer
                self._oieb.payload_write_pos = 0
                self._oieb.payload_written_count += 1  # Count the wrap marker

            # Write frame header
            header = FrameHeader(payload_size=size, sequence_number=self._sequence_number)
            payload_base = self._oieb_size + self._metadata_size
            header_offset = payload_base + self._oieb.payload_write_pos
            self._shm.write_bytes(header_offset, header.pack())

            # Store state for commit
            self._pending_write_pos = self._oieb.payload_write_pos + FrameHeader.SIZE
            self._pending_frame_size = size
            self._pending_total_size = total_size

            # Return memoryview for data area
            # Note: Lock is held until commit_frame() is called
            data_offset = payload_base + self._pending_write_pos
            return self._shm.get_memoryview(data_offset, size)
        except:
            # Release lock on error
            self._lock.release()
            raise

    def commit_frame(self) -> None:
        """
        Commit a frame after writing to buffer returned by get_frame_buffer

        Must be called to complete the write operation started by get_frame_buffer.
        """
        if not hasattr(self, "_pending_write_pos"):
            raise ZeroBufferException("No pending frame to commit")

        try:
            if not self._oieb:
                raise ZeroBufferException("Writer not properly initialized")

            # Update write position
            self._oieb.payload_write_pos = (
                self._pending_write_pos + self._pending_frame_size
            ) % self._oieb.payload_size
            self._sequence_number += 1
            self._frames_written += 1
            self._bytes_written += self._pending_frame_size

            # Update OIEB
            self._oieb.payload_free_bytes -= self._pending_total_size
            self._oieb.payload_written_count += 1

            # Flush shared memory to ensure all writes are visible
            self._shm.flush()

            # Signal reader
            self._sem_write.release()

        finally:
            # Clear pending state
            del self._pending_write_pos
            del self._pending_frame_size
            del self._pending_total_size

            # Must unlock here since we locked in get_frame_buffer
            self._lock.release()

    def _is_reader_connected(self) -> bool:
        """Check if reader is connected"""
        if not self._oieb:
            return False
        return self._oieb.reader_pid != 0 and platform.process_exists(self._oieb.reader_pid)

    def is_reader_connected(self) -> bool:
        """Check if reader is connected"""
        with self._lock:
            if self._closed:
                return False
            return self._is_reader_connected()

    @property
    def frames_written(self) -> int:
        """Get number of frames written"""
        return self._frames_written

    @property
    def bytes_written(self) -> int:
        """Get number of bytes written"""
        return self._bytes_written

    @property
    def write_timeout(self) -> timedelta:
        """
        Get the timeout for write operations when the buffer is full.

        Returns:
            Timeout as timedelta (default is 5 seconds)
        """
        return self._write_timeout

    @write_timeout.setter
    def write_timeout(self, value: timedelta) -> None:
        """
        Set the timeout for write operations when the buffer is full.

        Args:
            value: Timeout as timedelta (must be positive)

        Raises:
            ValueError: If timeout is not positive
        """
        if value <= timedelta():
            raise ValueError("Write timeout must be positive")
        self._write_timeout = value

    def close(self) -> None:
        """Close the writer and clean up resources"""
        with self._lock:
            if self._closed:
                return

            self._closed = True

            # Clear writer PID
            try:
                if self._oieb:
                    self._oieb.writer_pid = 0
            except Exception:
                pass

            # Properly dispose the OIEBView to release its memoryview
            if self._oieb:
                self._oieb.dispose()
                self._oieb = None

            # Close resources (writer doesn't own them)
            if hasattr(self, "_sem_read"):
                self._sem_read.close()

            if hasattr(self, "_sem_write"):
                self._sem_write.close()

            if hasattr(self, "_shm"):
                self._shm.close()

    def __enter__(self) -> "Writer":
        return self

    def __exit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        self.close()

    def __del__(self) -> None:
        self.close()
