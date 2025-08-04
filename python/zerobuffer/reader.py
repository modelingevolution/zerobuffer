"""
ZeroBuffer Reader implementation

Provides zero-copy reading from shared memory buffers.
"""

import os
import threading
from typing import Optional, Union
from pathlib import Path
import glob
import logging

from . import platform
from .types import OIEB, BufferConfig, Frame, FrameHeader, align_to_boundary
from .exceptions import (
    ZeroBufferException,
    WriterDeadException,
    SequenceError
)
from .logging_config import LoggerMixin


class Reader(LoggerMixin):
    """
    Zero-copy reader for ZeroBuffer
    
    The Reader creates and owns the shared memory buffer. It waits for a Writer
    to connect and then reads frames with zero-copy access.
    """
    
    def __init__(self, name: str, config: Optional[BufferConfig] = None, logger: Optional[logging.Logger] = None):
        """
        Create a new ZeroBuffer reader
        
        Args:
            name: Name of the buffer
            config: Buffer configuration (uses defaults if not provided)
            logger: Optional logger instance (creates one if not provided)
        """
        self.name = name
        self._config = config or BufferConfig()
        self._lock = threading.RLock()
        self._closed = False
        self._expected_sequence = 1
        self._frames_read = 0
        self._bytes_read = 0
        self._current_frame_size = 0
        
        # Set logger if provided, otherwise use mixin
        if logger:
            self._logger_instance = logger
        
        self._logger.debug("Creating Reader with name=%s, config=%s", name, self._config)
        
        
        # Calculate aligned sizes
        self._oieb_size = align_to_boundary(OIEB.SIZE)
        self._metadata_size = align_to_boundary(self._config.metadata_size)
        self._payload_size = align_to_boundary(self._config.payload_size)
        
        total_size = self._oieb_size + self._metadata_size + self._payload_size
        
        # Clean up stale resources
        self._logger.debug("Cleaning up stale resources before creating buffer")
        self._cleanup_stale_resources()
        
        # Create lock file
        lock_path = Path(platform.get_temp_directory()) / f"{name}.lock"
        self._lock_file = platform.create_file_lock(str(lock_path))
        
        try:
            # Create shared memory
            self._logger.info("Creating shared memory: name=%s, size=%d bytes", name, total_size)
            self._shm = platform.create_shared_memory(name, total_size)
            self._buffer = self._shm.get_buffer()
            
            # Create memory views for each section
            self._oieb_view = self._buffer[:self._oieb_size]
            self._metadata_view = self._buffer[self._oieb_size:self._oieb_size + self._metadata_size]
            self._payload_view = self._buffer[self._oieb_size + self._metadata_size:]
            
            # Initialize OIEB
            oieb = OIEB(
                operation_size=self._oieb_size,
                metadata_size=self._metadata_size,
                metadata_free_bytes=self._metadata_size,
                metadata_written_bytes=0,
                payload_size=self._payload_size,
                payload_free_bytes=self._payload_size,
                payload_write_pos=0,
                payload_read_pos=0,
                payload_written_count=0,
                payload_read_count=0,
                writer_pid=0,
                reader_pid=os.getpid()
            )
            self._oieb_view[:] = oieb.pack()
            
            # Create semaphores
            self._logger.debug("Creating semaphores: write=%s, read=%s", f"sem-w-{name}", f"sem-r-{name}")
            self._sem_write = platform.create_semaphore(f"sem-w-{name}", 0)
            self._sem_read = platform.create_semaphore(f"sem-r-{name}", 0)
            
            self._logger.info("Reader created successfully: pid=%d", os.getpid())
            
        except Exception as e:
            self._logger.error("Failed to create Reader: %s", e)
            self._cleanup_on_error()
            raise
    
    def _cleanup_stale_resources(self):
        """Clean up stale resources from dead processes"""
        lock_dir = Path(platform.get_temp_directory())
        
        try:
            lock_dir.mkdir(parents=True, exist_ok=True)
            
            for lock_file in lock_dir.glob("*.lock"):
                # Use the platform-specific file lock implementation
                if hasattr(platform, 'PlatformFileLock'):
                    try_remove = platform.PlatformFileLock.try_remove_stale
                else:
                    # Fallback to Linux implementation if available
                    try_remove = getattr(platform.LinuxFileLock, 'try_remove_stale', lambda x: False)
                
                if try_remove(str(lock_file)):
                    # We removed a stale lock, clean up associated resources
                    buffer_name = lock_file.stem
                    self._logger.debug("Found stale lock file for buffer: %s", buffer_name)
                    
                    try:
                        # Check if shared memory exists and is orphaned
                        shm = platform.open_shared_memory(buffer_name)
                        buffer = shm.get_buffer()
                        oieb = OIEB.unpack(buffer[:OIEB.SIZE])
                        
                        # Check if both reader and writer are dead
                        reader_dead = (oieb.reader_pid == 0 or 
                                     not platform.process_exists(oieb.reader_pid))
                        writer_dead = (oieb.writer_pid == 0 or 
                                     not platform.process_exists(oieb.writer_pid))
                        
                        if reader_dead and writer_dead:
                            # Both processes are dead, safe to clean up
                            self._logger.info("Cleaning up orphaned buffer: %s (reader_pid=%d, writer_pid=%d)", 
                                            buffer_name, oieb.reader_pid, oieb.writer_pid)
                            shm.close()
                            shm.unlink()
                            
                            # Clean up semaphores
                            try:
                                sem = platform.open_semaphore(f"sem-w-{buffer_name}")
                                sem.close()
                                sem.unlink()
                            except:
                                pass
                            
                            try:
                                sem = platform.open_semaphore(f"sem-r-{buffer_name}")
                                sem.close()
                                sem.unlink()
                            except:
                                pass
                    except:
                        # If we can't open shared memory, clean up anyway
                        pass
        except Exception:
            # Ignore errors during cleanup
            pass
    
    def _cleanup_on_error(self):
        """Clean up resources on initialization error"""
        if hasattr(self, '_sem_read'):
            self._sem_read.close()
            self._sem_read.unlink()
        if hasattr(self, '_sem_write'):
            self._sem_write.close()
            self._sem_write.unlink()
        if hasattr(self, '_shm'):
            self._shm.close()
            self._shm.unlink()
        if hasattr(self, '_lock_file'):
            self._lock_file.close()
    
    def _read_oieb(self) -> OIEB:
        """Read current OIEB from shared memory"""
        return OIEB.unpack(self._oieb_view)
    
    def _write_oieb(self, oieb: OIEB) -> None:
        """Write OIEB to shared memory"""
        self._oieb_view[:] = oieb.pack()
    
    
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
            
            oieb = self._read_oieb()
            if oieb.metadata_written_bytes == 0:
                return None
            
            # Read metadata size prefix
            size_bytes = self._metadata_view[:8]
            size = int.from_bytes(size_bytes, byteorder='little')
            
            if size == 0 or size > oieb.metadata_written_bytes - 8:
                raise ZeroBufferException("Invalid metadata size")
            
            # Return view of actual metadata (skip size prefix)
            return self._metadata_view[8:8 + size]
    
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
        self._logger.debug("ReadFrame called with timeout=%s", timeout)
        
        with self._lock:
            if self._closed:
                raise ZeroBufferException("Reader is closed")
            
            while True:
                # Check if data is already available before waiting
                oieb = self._read_oieb()
                
                self._logger.debug("OIEB state: WrittenCount=%d, ReadCount=%d, WritePos=%d, ReadPos=%d, FreeBytes=%d, PayloadSize=%d",
                                 oieb.payload_written_count, oieb.payload_read_count, oieb.payload_write_pos, 
                                 oieb.payload_read_pos, oieb.payload_free_bytes, oieb.payload_size)
                
                # If no data available, wait for it
                if oieb.payload_written_count <= oieb.payload_read_count:
                    # Wait for data with timeout
                    if not self._sem_write.acquire(timeout):
                        # Timeout - check if writer is alive
                        if oieb.writer_pid != 0 and not platform.process_exists(oieb.writer_pid):
                            self._logger.warning("Writer process %d is dead", oieb.writer_pid)
                            raise WriterDeadException()
                        self._logger.debug("Read timeout after %s seconds", timeout)
                        return None  # Timeout
                    
                    # Re-read OIEB after semaphore
                    oieb = self._read_oieb()
                    
                    # Double-check if there's data to read
                    if (oieb.payload_read_pos == oieb.payload_write_pos and 
                        oieb.payload_written_count == oieb.payload_read_count):
                        # No new data (spurious wakeup?)
                        continue
                
                # Check if we need to wrap to follow the writer
                if (oieb.payload_write_pos < oieb.payload_read_pos and 
                    oieb.payload_written_count > oieb.payload_read_count):
                    # Writer has wrapped, we need to wrap too if there's not enough space
                    if oieb.payload_read_pos + FrameHeader.SIZE > oieb.payload_size:
                        oieb.payload_read_pos = 0
                        self._write_oieb(oieb)  # Update OIEB in shared memory
                
                # Read frame header
                header_offset = oieb.payload_read_pos
                header_data = self._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                header = FrameHeader.unpack(bytes(header_data))
                
                # Check for wrap-around marker
                if header.payload_size == 0:
                    # This is a wrap marker
                    self._logger.debug("Found wrap marker at position %d, handling wrap-around", oieb.payload_read_pos)
                    
                    # Calculate wasted space from current read position to end of buffer
                    wasted_space = oieb.payload_size - oieb.payload_read_pos
                    self._logger.debug("Wrap-around: wasted space = %d bytes (from %d to %d)", 
                                     wasted_space, oieb.payload_read_pos, oieb.payload_size)
                    
                    # Add back the wasted space to free bytes
                    oieb.payload_free_bytes += wasted_space
                    
                    # Move to beginning of buffer
                    oieb.payload_read_pos = 0
                    oieb.payload_read_count += 1  # Count the wrap marker as a "frame"
                    
                    self._write_oieb(oieb)  # Update OIEB in shared memory
                    
                    self._logger.debug("After wrap: ReadPos=0, ReadCount=%d, FreeBytes=%d", 
                                     oieb.payload_read_count, oieb.payload_free_bytes)
                    
                    # Signal that we consumed the wrap marker (freed space)
                    self._sem_read.release()
                    
                    # Continue to read the actual frame at the beginning
                    continue
                
                # Validate sequence number
                if header.sequence_number != self._expected_sequence:
                    self._logger.error("Sequence error: expected %d, got %d", 
                                     self._expected_sequence, header.sequence_number)
                    raise SequenceError(self._expected_sequence, header.sequence_number)
                
                # Validate frame size
                if header.payload_size == 0:
                    self._logger.error("Invalid frame size: 0")
                    raise ZeroBufferException("Invalid frame size: 0")
                
                total_frame_size = FrameHeader.SIZE + header.payload_size
                
                self._logger.debug("Reading frame: seq=%d, size=%d from position %d", 
                                 header.sequence_number, header.payload_size, oieb.payload_read_pos)
                
                # Check if frame wraps around buffer
                if oieb.payload_read_pos + total_frame_size > oieb.payload_size:
                    # Frame would extend beyond buffer
                    if oieb.payload_write_pos < oieb.payload_read_pos:
                        # Writer has wrapped, we should wrap too
                        oieb.payload_read_pos = 0
                        self._write_oieb(oieb)  # Update OIEB in shared memory
                        # Re-read header at new position
                        header_offset = 0
                        header_data = self._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                        header = FrameHeader.unpack(bytes(header_data))
                        
                        # Re-validate sequence number after wrap
                        if header.sequence_number != self._expected_sequence:
                            raise SequenceError(self._expected_sequence, header.sequence_number)
                    else:
                        # Writer hasn't wrapped yet, wait
                        continue
                
                # Create frame reference (zero-copy)
                data_offset = header_offset + FrameHeader.SIZE
                frame = Frame(
                    memory_view=self._payload_view,
                    offset=data_offset,
                    size=header.payload_size,
                    sequence=header.sequence_number
                )
                
                # Update OIEB immediately (like C++ and C# do)
                old_pos = oieb.payload_read_pos
                oieb.payload_read_pos += total_frame_size
                if oieb.payload_read_pos >= oieb.payload_size:
                    oieb.payload_read_pos -= oieb.payload_size
                oieb.payload_read_count += 1
                oieb.payload_free_bytes += total_frame_size
                
                self._logger.debug("Updating read position: %d -> %d", old_pos, oieb.payload_read_pos)
                
                self._write_oieb(oieb)
                
                self._logger.debug("Frame read complete: seq=%d, new state: ReadCount=%d, FreeBytes=%d", 
                                 header.sequence_number, oieb.payload_read_count, oieb.payload_free_bytes)
                
                # Signal writer that space is available
                self._sem_read.release()
                
                # Update tracking
                self._current_frame_size = total_frame_size
                self._expected_sequence += 1
                self._frames_read += 1
                self._bytes_read += header.payload_size
                
                return frame
    
    def release_frame(self, frame: Frame) -> None:
        """
        Release frame and free buffer space
        
        No-op: All updates are now done in read_frame() to match C++/C# behavior.
        This method is kept for API compatibility.
        
        Args:
            frame: Frame to release
        """
        # No-op: All updates are now done in read_frame()
        pass
    
    def is_writer_connected(self) -> bool:
        """Check if a writer is connected"""
        with self._lock:
            if self._closed:
                return False
            
            oieb = self._read_oieb()
            return oieb.writer_pid != 0 and platform.process_exists(oieb.writer_pid)
    
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
            
            self._logger.info("Closing Reader: frames_read=%d, bytes_read=%d", 
                            self._frames_read, self._bytes_read)
            
            self._closed = True
            
            # Clear reader PID
            try:
                oieb = self._read_oieb()
                oieb.reader_pid = 0
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
            
            # Close and unlink resources (reader owns them)
            if hasattr(self, '_sem_read'):
                self._sem_read.close()
                self._sem_read.unlink()
            
            if hasattr(self, '_sem_write'):
                self._sem_write.close()
                self._sem_write.unlink()
            
            if hasattr(self, '_shm'):
                self._shm.close()
                self._shm.unlink()
            
            if hasattr(self, '_lock_file'):
                self._lock_file.close()
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()
    
    def __del__(self):
        self.close()