"""
ZeroBuffer Writer implementation

Provides zero-copy writing to shared memory buffers.
"""

import os
import threading
from typing import Optional, Union
import logging

from . import platform
from .types import OIEB, Frame, FrameHeader
from .exceptions import (
    ZeroBufferException,
    ReaderDeadException,
    WriterAlreadyConnectedException,
    FrameTooLargeException,
    InvalidFrameSizeException,
    MetadataAlreadyWrittenException
)
from .logging_config import LoggerMixin


class Writer(LoggerMixin):
    """
    Zero-copy writer for ZeroBuffer
    
    The Writer connects to an existing buffer created by a Reader and writes
    frames with zero-copy operations when possible.
    """
    
    def __init__(self, name: str, logger: Optional[logging.Logger] = None):
        """
        Connect to an existing ZeroBuffer
        
        Args:
            name: Name of the buffer to connect to
            logger: Optional logger instance (creates one if not provided)
            
        Raises:
            ZeroBufferException: If buffer doesn't exist
            WriterAlreadyConnectedException: If another writer is connected
        """
        self.name = name
        self._lock = threading.RLock()
        self._closed = False
        self._sequence_number = 1
        self._frames_written = 0
        self._bytes_written = 0
        self._metadata_written = False
        
        # Set logger if provided, otherwise use mixin
        if logger:
            self._logger_instance = logger
        
        self._logger.debug("Creating Writer for buffer: %s", name)
        
        
        try:
            # Open existing shared memory
            self._shm = platform.open_shared_memory(name)
            self._buffer = self._shm.get_buffer()
            
            # Read OIEB to get layout
            oieb = OIEB.unpack(self._buffer[:OIEB.SIZE])
            
            # Verify OIEB
            if oieb.operation_size != OIEB.SIZE:
                raise ZeroBufferException("Invalid OIEB size - version mismatch?")
            
            # Check if reader exists
            if oieb.reader_pid == 0 or not platform.process_exists(oieb.reader_pid):
                raise ZeroBufferException("No active reader found")
            
            # Check if another writer exists
            if oieb.writer_pid != 0 and platform.process_exists(oieb.writer_pid):
                raise WriterAlreadyConnectedException()
            
            # Set writer PID
            oieb.writer_pid = os.getpid()
            self._buffer[:OIEB.SIZE] = oieb.pack()
            
            # Create memory views
            self._oieb_size = oieb.operation_size
            self._metadata_size = oieb.metadata_size
            self._payload_size = oieb.payload_size
            
            self._oieb_view = self._buffer[:self._oieb_size]
            self._metadata_view = self._buffer[self._oieb_size:self._oieb_size + self._metadata_size]
            self._payload_view = self._buffer[self._oieb_size + self._metadata_size:]
            
            # Open semaphores
            self._sem_write = platform.open_semaphore(f"sem-w-{name}")
            self._sem_read = platform.open_semaphore(f"sem-r-{name}")
            
            # Check if metadata was already written
            self._metadata_written = (oieb.metadata_written_bytes > 0)
            
        except Exception:
            self._cleanup_on_error()
            raise
    
    def _cleanup_on_error(self):
        """Clean up resources on initialization error"""
        if hasattr(self, '_sem_read'):
            self._sem_read.close()
        if hasattr(self, '_sem_write'):
            self._sem_write.close()
        if hasattr(self, '_shm'):
            self._shm.close()
    
    def _read_oieb(self) -> OIEB:
        """Read current OIEB from shared memory"""
        return OIEB.unpack(self._oieb_view)
    
    def _write_oieb(self, oieb: OIEB) -> None:
        """Write OIEB to shared memory"""
        self._oieb_view[:] = oieb.pack()
    
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
            
            oieb = self._read_oieb()
            
            # Check size with header
            total_size = 8 + len(data)  # 8 bytes for size prefix
            if total_size > oieb.metadata_size:
                raise ZeroBufferException("Metadata too large for buffer")
            
            # Write size prefix
            self._metadata_view[:8] = len(data).to_bytes(8, byteorder='little')
            
            # Write metadata
            if len(data) > 0:
                self._metadata_view[8:8 + len(data)] = data
            
            # Update OIEB
            oieb.metadata_written_bytes = total_size
            oieb.metadata_free_bytes = oieb.metadata_size - total_size
            self._write_oieb(oieb)
            
            self._metadata_written = True
    
    def _calculate_used_bytes(self, write_pos: int, read_pos: int, buffer_size: int) -> int:
        """Calculate used bytes in circular buffer"""
        if write_pos >= read_pos:
            return write_pos - read_pos
        else:
            return buffer_size - read_pos + write_pos
    
    def _get_continuous_free_space(self, oieb: OIEB) -> int:
        """Calculate continuous free space in buffer"""
        if oieb.payload_write_pos >= oieb.payload_read_pos:
            # Write ahead of read - check space to end and beginning
            space_to_end = oieb.payload_size - oieb.payload_write_pos
            if oieb.payload_read_pos == 0:
                # Can't wrap if reader at beginning
                return space_to_end
            # Can use space at beginning if we wrap
            return max(space_to_end, oieb.payload_read_pos)
        else:
            # Read ahead of write - continuous space until read pos
            return oieb.payload_read_pos - oieb.payload_write_pos
    
    
    def write_frame(self, data: Union[bytes, bytearray, memoryview]) -> None:
        """
        Write a frame to the buffer
        
        This method copies the data into the shared memory buffer.
        For true zero-copy writing with memoryview, use write_frame_zero_copy.
        
        Args:
            data: Frame data to write
            
        Raises:
            InvalidFrameSizeException: If data is empty
            FrameTooLargeException: If frame is too large for buffer
            ReaderDeadException: If reader process died
        """
        if len(data) == 0:
            raise InvalidFrameSizeException()
        
        self._logger.debug("WriteFrame called with data size=%d", len(data))
        
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Writer is closed")
            
            frame_size = len(data)
            total_size = FrameHeader.SIZE + frame_size
            
            while True:
                oieb = self._read_oieb()
                
                # Check if frame is too large
                if total_size > oieb.payload_size:
                    raise FrameTooLargeException()
                
                # Check if reader is still alive
                if not self._is_reader_connected(oieb):
                    raise ReaderDeadException()
                
                # Check for available space
                if oieb.payload_free_bytes >= total_size:
                    # We have enough space
                    break
                
                # Wait for reader to free space (blocking)
                if not self._sem_read.acquire(timeout=5.0):
                    # Timeout - check if reader is alive
                    if not self._is_reader_connected(oieb):
                        raise ReaderDeadException()
                    # Continue waiting (no exception, just keep blocking)
            
            # Check if we need to wrap
            continuous_free = self._get_continuous_free_space(oieb)
            space_to_end = oieb.payload_size - oieb.payload_write_pos
            
            # We need to wrap if frame doesn't fit in continuous space
            if continuous_free >= total_size and space_to_end < total_size and oieb.payload_read_pos > 0:
                # Need to wrap to beginning
                # Write a special marker if there's space for at least a header
                if space_to_end >= FrameHeader.SIZE:
                    # Write wrap marker header
                    wrap_header = FrameHeader(payload_size=0, sequence_number=0)
                    wrap_offset = oieb.payload_write_pos
                    self._payload_view[wrap_offset:wrap_offset + FrameHeader.SIZE] = wrap_header.pack()
                
                # Account for the wasted space at the end
                oieb.payload_free_bytes -= space_to_end
                
                # Move to beginning of buffer
                oieb.payload_write_pos = 0
                oieb.payload_written_count += 1  # Count the wrap marker
            
            # Write frame header
            header = FrameHeader(
                payload_size=frame_size,
                sequence_number=self._sequence_number
            )
            header_offset = oieb.payload_write_pos
            self._payload_view[header_offset:header_offset + FrameHeader.SIZE] = header.pack()
            
            # Write frame data
            data_offset = header_offset + FrameHeader.SIZE
            self._payload_view[data_offset:data_offset + frame_size] = data
            
            # Update tracking
            oieb.payload_write_pos += total_size
            self._sequence_number += 1
            self._frames_written += 1
            self._bytes_written += frame_size
            
            # Update OIEB
            oieb.payload_free_bytes -= total_size
            oieb.payload_written_count += 1
            
            # Memory barrier equivalent (handled by lock)
            
            self._write_oieb(oieb)
            
            # Signal reader
            self._sem_write.release()
    
    def write_frame_zero_copy(self, data: memoryview) -> None:
        """
        Write a frame with true zero-copy operation
        
        This method requires the data to be a memoryview and directly copies
        from the source buffer to shared memory without intermediate copies.
        
        Args:
            data: Frame data as memoryview
            
        Raises:
            TypeError: If data is not a memoryview
            InvalidFrameSizeException: If data is empty
            FrameTooLargeException: If frame is too large for buffer
            ReaderDeadException: If reader process died
        """
        if not isinstance(data, memoryview):
            raise TypeError("write_frame_zero_copy requires memoryview for zero-copy operation")
        
        # Use the same implementation as write_frame since memoryview assignment
        # in Python is already zero-copy when possible
        self.write_frame(data)
    
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
            
            total_size = FrameHeader.SIZE + size
            
            # Wait for space (same logic as write_frame)
            while True:
                oieb = self._read_oieb()
                
                if total_size > oieb.payload_size:
                    raise FrameTooLargeException()
                
                if not self._is_reader_connected(oieb):
                    raise ReaderDeadException()
                
                # Check for available space
                if oieb.payload_free_bytes >= total_size:
                    break
                
                if not self._sem_read.acquire(timeout=5.0):
                    if not self._is_reader_connected(oieb):
                        raise ReaderDeadException()
                    # Continue waiting (no exception, just keep blocking)
            
            # Handle wrap-around if needed
            continuous_free = self._get_continuous_free_space(oieb)
            space_to_end = oieb.payload_size - oieb.payload_write_pos
            
            # We need to wrap if frame doesn't fit in continuous space
            if continuous_free >= total_size and space_to_end < total_size and oieb.payload_read_pos > 0:
                if space_to_end >= FrameHeader.SIZE:
                    # Write wrap marker header
                    wrap_header = FrameHeader(payload_size=0, sequence_number=0)
                    wrap_offset = oieb.payload_write_pos
                    self._payload_view[wrap_offset:wrap_offset + FrameHeader.SIZE] = wrap_header.pack()
                
                # Account for the wasted space at the end
                oieb.payload_free_bytes -= space_to_end
                
                # Move to beginning of buffer
                oieb.payload_write_pos = 0
                oieb.payload_written_count += 1  # Count the wrap marker
            
            # Write frame header
            header = FrameHeader(
                payload_size=size,
                sequence_number=self._sequence_number
            )
            header_offset = oieb.payload_write_pos
            self._payload_view[header_offset:header_offset + FrameHeader.SIZE] = header.pack()
            
            # Store state for commit
            self._pending_write_pos = header_offset + FrameHeader.SIZE
            self._pending_frame_size = size
            self._pending_total_size = total_size
            
            # Return memoryview for data area
            # Note: Lock is held until commit_frame() is called
            return self._payload_view[self._pending_write_pos:self._pending_write_pos + size]
        except:
            # Release lock on error
            self._lock.release()
            raise
    
    def commit_frame(self) -> None:
        """
        Commit a frame after writing to buffer returned by get_frame_buffer
        
        Must be called to complete the write operation started by get_frame_buffer.
        """
        if not hasattr(self, '_pending_write_pos'):
            raise ZeroBufferException("No pending frame to commit")
        
        try:
            oieb = self._read_oieb()
            
            # Update write position
            oieb.payload_write_pos = (self._pending_write_pos + self._pending_frame_size) % oieb.payload_size
            self._sequence_number += 1
            self._frames_written += 1
            self._bytes_written += self._pending_frame_size
            
            # Update OIEB
            oieb.payload_free_bytes -= self._pending_total_size
            oieb.payload_written_count += 1
            
            self._write_oieb(oieb)
            
            # Signal reader
            self._sem_write.release()
            
        finally:
            # Clear pending state
            del self._pending_write_pos
            del self._pending_frame_size
            del self._pending_total_size
            
            # Must unlock here since we locked in get_frame_buffer
            self._lock.release()
    
    def _is_reader_connected(self, oieb: Optional[OIEB] = None) -> bool:
        """Check if reader is connected"""
        if oieb is None:
            oieb = self._read_oieb()
        return oieb.reader_pid != 0 and platform.process_exists(oieb.reader_pid)
    
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
    
    def close(self) -> None:
        """Close the writer and clean up resources"""
        with self._lock:
            if self._closed:
                return
            
            self._closed = True
            
            # Clear writer PID
            try:
                oieb = self._read_oieb()
                oieb.writer_pid = 0
                self._write_oieb(oieb)
            except:
                pass
            
            # Release memoryviews first
            if hasattr(self, '_oieb_view') and self._oieb_view is not None:
                self._oieb_view.release()
                self._oieb_view = None
            if hasattr(self, '_metadata_view') and self._metadata_view is not None:
                self._metadata_view.release()
                self._metadata_view = None
            if hasattr(self, '_payload_view') and self._payload_view is not None:
                self._payload_view.release()
                self._payload_view = None
            
            # Clear buffer reference
            if hasattr(self, '_buffer') and self._buffer is not None:
                self._buffer = None
            
            # Close resources (writer doesn't own them)
            if hasattr(self, '_sem_read'):
                self._sem_read.close()
            
            if hasattr(self, '_sem_write'):
                self._sem_write.close()
            
            if hasattr(self, '_shm'):
                self._shm.close()
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()
    
    def __del__(self):
        self.close()