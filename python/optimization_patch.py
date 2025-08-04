"""
Optimization patch for ZeroBuffer Python implementation

This module provides optimized versions of critical functions that can be
monkey-patched into the existing implementation for immediate performance gains.

Usage:
    import zerobuffer
    from optimization_patch import apply_optimizations
    apply_optimizations()
"""

import struct
import threading
from typing import Union

# Pre-compiled struct formats for better performance
OIEB_STRUCT = struct.Struct('<6Q4q4Q')
HEADER_STRUCT = struct.Struct('<2Q')
UINT64_STRUCT = struct.Struct('<Q')

# Thread-local storage for buffers
_thread_local = threading.local()


def _get_thread_buffers():
    """Get or create thread-local buffers"""
    if not hasattr(_thread_local, 'initialized'):
        _thread_local.header_buffer = bytearray(16)
        _thread_local.oieb_buffer = bytearray(128)
        _thread_local.header_view = memoryview(_thread_local.header_buffer)
        _thread_local.initialized = True
    return _thread_local


def optimized_write_frame(self, data: Union[bytes, bytearray, memoryview]) -> None:
    """Optimized write_frame with reduced allocations"""
    from zerobuffer.exceptions import (
        InvalidFrameSizeException, FrameTooLargeException,
        ReaderDeadException, ZeroBufferException
    )
    from zerobuffer.types import FrameHeader
    
    if len(data) == 0:
        raise InvalidFrameSizeException()
    
    # Get thread-local buffers
    tls = _get_thread_buffers()
    header_buffer = tls.header_buffer
    
    with self._lock:
        if self._closed:
            raise ZeroBufferException("Writer is closed")
        
        frame_size = len(data)
        total_size = FrameHeader.SIZE + frame_size
        
        while True:
            oieb = self._read_oieb()
            
            if total_size > oieb.payload_size:
                raise FrameTooLargeException()
            
            if not self._is_reader_connected(oieb):
                raise ReaderDeadException()
            
            if oieb.payload_free_bytes >= total_size:
                break
            
            if not self._sem_read.acquire(timeout=5.0):
                if not self._is_reader_connected(oieb):
                    raise ReaderDeadException()
        
        # Check if we need to wrap
        continuous_free = self._get_continuous_free_space(oieb)
        space_to_end = oieb.payload_size - oieb.payload_write_pos
        
        if continuous_free >= total_size and space_to_end < total_size and oieb.payload_read_pos > 0:
            # Need to wrap to beginning
            if space_to_end >= FrameHeader.SIZE:
                # Write wrap marker directly
                wrap_offset = oieb.payload_write_pos
                HEADER_STRUCT.pack_into(header_buffer, 0, 0, 0)
                self._payload_view[wrap_offset:wrap_offset + FrameHeader.SIZE] = header_buffer
            
            oieb.payload_free_bytes -= space_to_end
            oieb.payload_write_pos = 0
            oieb.payload_written_count += 1
        
        # Write frame header directly
        header_offset = oieb.payload_write_pos
        HEADER_STRUCT.pack_into(header_buffer, 0, frame_size, self._sequence_number)
        self._payload_view[header_offset:header_offset + FrameHeader.SIZE] = header_buffer
        
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
        
        self._write_oieb(oieb)
        self._sem_write.release()


def optimized_read_frame(self, timeout: float = 5.0):
    """Optimized read_frame with reduced allocations"""
    from zerobuffer.exceptions import (
        WriterDeadException, ZeroBufferException, SequenceError
    )
    from zerobuffer.types import FrameHeader, Frame
    from zerobuffer import platform
    
    # Get thread-local buffers
    tls = _get_thread_buffers()
    header_view = tls.header_view
    
    with self._lock:
        if self._closed:
            raise ZeroBufferException("Reader is closed")
        
        while True:
            oieb = self._read_oieb()
            
            if oieb.payload_written_count <= oieb.payload_read_count:
                if not self._sem_write.acquire(timeout):
                    if oieb.writer_pid != 0 and not platform.process_exists(oieb.writer_pid):
                        raise WriterDeadException()
                    return None
                
                oieb = self._read_oieb()
                
                if (oieb.payload_read_pos == oieb.payload_write_pos and 
                    oieb.payload_written_count == oieb.payload_read_count):
                    continue
            
            # Check if we need to wrap
            if (oieb.payload_write_pos < oieb.payload_read_pos and 
                oieb.payload_written_count > oieb.payload_read_count):
                if oieb.payload_read_pos + FrameHeader.SIZE > oieb.payload_size:
                    oieb.payload_read_pos = 0
                    self._write_oieb(oieb)
            
            # Read frame header directly
            header_offset = oieb.payload_read_pos
            header_data = self._payload_view[header_offset:header_offset + FrameHeader.SIZE]
            
            # Copy to thread-local buffer and unpack
            header_view[:] = header_data
            payload_size, sequence_number = HEADER_STRUCT.unpack_from(header_view, 0)
            
            # Check for wrap-around marker
            if payload_size == 0:
                wasted_space = oieb.payload_size - oieb.payload_read_pos
                oieb.payload_free_bytes += wasted_space
                oieb.payload_read_pos = 0
                oieb.payload_read_count += 1
                
                self._write_oieb(oieb)
                self._sem_read.release()
                continue
            
            # Validate sequence number
            if sequence_number != self._expected_sequence:
                raise SequenceError(self._expected_sequence, sequence_number)
            
            if payload_size == 0:
                raise ZeroBufferException("Invalid frame size: 0")
            
            total_frame_size = FrameHeader.SIZE + payload_size
            
            # Check if frame wraps around buffer
            if oieb.payload_read_pos + total_frame_size > oieb.payload_size:
                if oieb.payload_write_pos < oieb.payload_read_pos:
                    oieb.payload_read_pos = 0
                    self._write_oieb(oieb)
                    
                    # Re-read header at new position
                    header_offset = 0
                    header_data = self._payload_view[header_offset:header_offset + FrameHeader.SIZE]
                    header_view[:] = header_data
                    payload_size, sequence_number = HEADER_STRUCT.unpack_from(header_view, 0)
                    
                    if sequence_number != self._expected_sequence:
                        raise SequenceError(self._expected_sequence, sequence_number)
                else:
                    continue
            
            # Create frame reference (zero-copy)
            data_offset = header_offset + FrameHeader.SIZE
            frame = Frame(
                memory_view=self._payload_view,
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
            
            self._write_oieb(oieb)
            self._sem_read.release()
            
            # Update tracking
            self._current_frame_size = total_frame_size
            self._expected_sequence += 1
            self._frames_read += 1
            self._bytes_read += payload_size
            
            return frame


def apply_optimizations():
    """Apply optimizations to the zerobuffer module"""
    import zerobuffer
    
    # Patch Writer class
    zerobuffer.Writer.write_frame_original = zerobuffer.Writer.write_frame
    zerobuffer.Writer.write_frame = optimized_write_frame
    
    # Patch Reader class  
    zerobuffer.Reader.read_frame_original = zerobuffer.Reader.read_frame
    zerobuffer.Reader.read_frame = optimized_read_frame
    
    print("ZeroBuffer optimizations applied successfully!")
    print("- Pre-compiled struct formats")
    print("- Thread-local buffer reuse")
    print("- Direct pack_into operations")
    print("- Reduced object allocations")


if __name__ == "__main__":
    # Example usage
    apply_optimizations()