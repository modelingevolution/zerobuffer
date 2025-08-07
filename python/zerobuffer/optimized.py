"""
Optimized ZeroBuffer implementation with reduced memory allocations

Key optimizations:
1. Pre-allocated buffers for struct operations
2. Reusable header objects
3. Direct memoryview operations without bytes() conversions
4. Cached struct.Struct instances
"""

import struct
from typing import Union, Optional, Tuple, TYPE_CHECKING
import threading

from .types import OIEB, FrameHeader, Frame
from .exceptions import *

if TYPE_CHECKING:
    from .reader import Reader
    from .writer import Writer


# Pre-compiled struct formats for better performance
OIEB_STRUCT = struct.Struct('<6Q4q4Q')  # OIEB format
HEADER_STRUCT = struct.Struct('<2Q')     # Frame header format
SIZE_STRUCT = struct.Struct('<Q')        # Single uint64 for metadata size


class OptimizedWriter:
    """Optimized Writer with reduced memory allocations"""
    
    def __init__(self, base_writer: 'Writer') -> None:
        self._writer = base_writer
        
        # Pre-allocate buffers for struct operations
        self._header_buffer = bytearray(HEADER_STRUCT.size)
        self._oieb_buffer = bytearray(OIEB_STRUCT.size)
        
        # Reusable header object
        self._reusable_header = FrameHeader(0, 0)
        
        # Thread-local storage for per-thread buffers
        self._thread_local = threading.local()
    
    def _get_thread_buffers(self) -> Tuple[bytearray, bytearray]:
        """Get thread-local buffers"""
        if not hasattr(self._thread_local, 'header_buffer'):
            self._thread_local.header_buffer = bytearray(HEADER_STRUCT.size)
            self._thread_local.oieb_buffer = bytearray(OIEB_STRUCT.size)
        return self._thread_local.header_buffer, self._thread_local.oieb_buffer
    
    def write_frame_optimized(self, data: Union[bytes, bytearray, memoryview]) -> None:
        """
        Optimized write_frame with minimal allocations
        """
        if len(data) == 0:
            raise InvalidFrameSizeException()
        
        with self._writer._lock:
            if self._writer._closed:
                raise ZeroBufferException("Writer is closed")
            
            frame_size = len(data)
            total_size = FrameHeader.SIZE + frame_size
            
            # Get thread-local buffers
            header_buffer, _ = self._get_thread_buffers()
            
            while True:
                oieb = self._writer._read_oieb()
                
                # Check if frame is too large
                if total_size > oieb.payload_size:
                    raise FrameTooLargeException()
                
                # Check if reader is still alive
                if not self._writer._is_reader_connected(oieb):
                    raise ReaderDeadException()
                
                # Check for available space
                if oieb.payload_free_bytes >= total_size:
                    break
                
                # Wait for reader to free space
                if not self._writer._sem_read.acquire(timeout=5.0):
                    if not self._writer._is_reader_connected(oieb):
                        raise ReaderDeadException()
            
            # Check if we need to wrap
            continuous_free = self._writer._get_continuous_free_space(oieb)
            space_to_end = oieb.payload_size - oieb.payload_write_pos
            
            # Handle wrap-around
            if continuous_free >= total_size and space_to_end < total_size and oieb.payload_read_pos > 0:
                # Need to wrap to beginning
                if space_to_end >= FrameHeader.SIZE:
                    # Write wrap marker header directly without creating objects
                    wrap_offset = oieb.payload_write_pos
                    HEADER_STRUCT.pack_into(header_buffer, 0, 0, 0)  # payload_size=0, sequence=0
                    self._writer._payload_view[wrap_offset:wrap_offset + FrameHeader.SIZE] = header_buffer
                
                # Account for wasted space
                oieb.payload_free_bytes -= space_to_end
                oieb.payload_write_pos = 0
                oieb.payload_written_count += 1
            
            # Write frame header directly without creating objects
            header_offset = oieb.payload_write_pos
            HEADER_STRUCT.pack_into(header_buffer, 0, frame_size, self._writer._sequence_number)
            self._writer._payload_view[header_offset:header_offset + FrameHeader.SIZE] = header_buffer
            
            # Write frame data
            data_offset = header_offset + FrameHeader.SIZE
            self._writer._payload_view[data_offset:data_offset + frame_size] = data
            
            # Update tracking
            oieb.payload_write_pos += total_size
            self._writer._sequence_number += 1
            self._writer._frames_written += 1
            self._writer._bytes_written += frame_size
            
            # Update OIEB
            oieb.payload_free_bytes -= total_size
            oieb.payload_written_count += 1
            
            self._writer._write_oieb(oieb)
            
            # Signal reader
            self._writer._sem_write.release()


class OptimizedReader:
    """Optimized Reader with reduced memory allocations"""
    
    def __init__(self, base_reader: 'Reader') -> None:
        self._reader = base_reader
        
        # Pre-allocate buffer for header unpacking
        self._header_view = memoryview(bytearray(HEADER_STRUCT.size))
        
        # Thread-local storage
        self._thread_local = threading.local()
    
    def _get_thread_header_view(self) -> memoryview:
        """Get thread-local header buffer"""
        if not hasattr(self._thread_local, 'header_view'):
            self._thread_local.header_view = memoryview(bytearray(HEADER_STRUCT.size))
        return self._thread_local.header_view
    
    def read_frame_optimized(self, timeout: float = 5.0) -> Optional[Frame]:
        """
        Optimized read_frame with minimal allocations
        """
        with self._reader._lock:
            if self._reader._closed:
                raise ZeroBufferException("Reader is closed")
            
            # Get thread-local buffer
            header_view = self._get_thread_header_view()
            
            while True:
                # Check if data is already available
                oieb = self._reader._read_oieb()
                
                # If no data available, wait for it
                if oieb.payload_written_count <= oieb.payload_read_count:
                    if not self._reader._sem_write.acquire(timeout):
                        if oieb.writer_pid != 0 and not self._reader.platform.process_exists(oieb.writer_pid):
                            raise WriterDeadException()
                        return None
                    
                    # Re-read OIEB after semaphore
                    oieb = self._reader._read_oieb()
                    
                    # Double-check if there's data to read
                    if (oieb.payload_read_pos == oieb.payload_write_pos and 
                        oieb.payload_written_count == oieb.payload_read_count):
                        continue
                
                # Check if we need to wrap
                if (oieb.payload_write_pos < oieb.payload_read_pos and 
                    oieb.payload_written_count > oieb.payload_read_count):
                    if oieb.payload_read_pos + FrameHeader.SIZE > oieb.payload_size:
                        oieb.payload_read_pos = 0
                        self._reader._write_oieb(oieb)
                
                # Read frame header directly into our buffer
                header_offset = oieb.payload_read_pos
                header_data = self._reader._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                
                # Copy into our thread-local buffer to avoid allocation
                header_view[:] = header_data
                
                # Unpack directly from the view
                payload_size, sequence_number = HEADER_STRUCT.unpack(header_view)
                
                # Check for wrap-around marker
                if payload_size == 0:
                    # This is a wrap marker
                    wasted_space = oieb.payload_size - oieb.payload_read_pos
                    oieb.payload_free_bytes += wasted_space
                    oieb.payload_read_pos = 0
                    oieb.payload_read_count += 1
                    
                    self._reader._write_oieb(oieb)
                    self._reader._sem_read.release()
                    continue
                
                # Validate sequence number
                if sequence_number != self._reader._expected_sequence:
                    raise SequenceError(self._reader._expected_sequence, sequence_number)
                
                # Validate frame size
                if payload_size == 0:
                    raise ZeroBufferException("Invalid frame size: 0")
                
                total_frame_size = FrameHeader.SIZE + payload_size
                
                # Check if frame wraps around buffer
                if oieb.payload_read_pos + total_frame_size > oieb.payload_size:
                    if oieb.payload_write_pos < oieb.payload_read_pos:
                        # Writer has wrapped, we should wrap too
                        oieb.payload_read_pos = 0
                        self._reader._write_oieb(oieb)
                        
                        # Re-read header at new position
                        header_offset = 0
                        header_data = self._reader._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                        header_view[:] = header_data
                        payload_size, sequence_number = HEADER_STRUCT.unpack(header_view)
                        
                        # Re-validate sequence
                        if sequence_number != self._reader._expected_sequence:
                            raise SequenceError(self._reader._expected_sequence, sequence_number)
                    else:
                        continue
                
                # Create frame reference (zero-copy)
                data_offset = header_offset + FrameHeader.SIZE
                frame = Frame(
                    memory_view=self._reader._payload_view,
                    offset=data_offset,
                    size=payload_size,
                    sequence=sequence_number
                )
                
                # Update OIEB immediately
                oieb.payload_read_pos += total_frame_size
                if oieb.payload_read_pos >= oieb.payload_size:
                    oieb.payload_read_pos -= oieb.payload_size
                oieb.payload_read_count += 1
                oieb.payload_free_bytes += total_frame_size
                
                self._reader._write_oieb(oieb)
                self._reader._sem_read.release()
                
                # Update tracking
                self._reader._current_frame_size = total_frame_size
                self._reader._expected_sequence += 1
                self._reader._frames_read += 1
                self._reader._bytes_read += payload_size
                
                return frame


def optimize_writer(writer: 'Writer') -> OptimizedWriter:
    """Wrap a Writer instance with optimized methods"""
    opt = OptimizedWriter(writer)
    # Monkey-patch the write_frame method
    setattr(writer, 'write_frame_original', writer.write_frame)
    setattr(writer, 'write_frame', opt.write_frame_optimized)
    return opt


def optimize_reader(reader: 'Reader') -> OptimizedReader:
    """Wrap a Reader instance with optimized methods"""
    opt = OptimizedReader(reader)
    # Monkey-patch the read_frame method
    setattr(reader, 'read_frame_original', reader.read_frame)
    setattr(reader, 'read_frame', opt.read_frame_optimized)
    return opt