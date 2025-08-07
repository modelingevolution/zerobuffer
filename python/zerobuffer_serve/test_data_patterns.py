"""
Shared test data patterns for consistent data generation across processes

This module provides the same test data generation patterns as the C# implementation
to ensure consistent data across Python and C# test processes.
"""


class TestDataPatterns:
    """Shared test data patterns for consistent data generation across processes"""
    
    @staticmethod
    def generate_frame_data(size: int, sequence: int) -> bytes:
        """
        Generate test data for a frame based on size and sequence number
        
        Args:
            size: Size of the frame data in bytes
            sequence: Sequence number of the frame
            
        Returns:
            Generated frame data as bytes
        """
        data = bytearray(size)
        for i in range(size):
            data[i] = (i + sequence) % 256
        return bytes(data)
    
    @staticmethod
    def generate_simple_frame_data(size: int) -> bytes:
        """
        Generate simple test data for a frame based only on size
        Used when sequence number is not known at write time
        
        Args:
            size: Size of the frame data in bytes
            
        Returns:
            Generated frame data as bytes
        """
        data = bytearray(size)
        for i in range(size):
            data[i] = i % 256
        return bytes(data)
    
    @staticmethod
    def verify_simple_frame_data(data: bytes) -> bool:
        """
        Verify that frame data matches the simple pattern
        
        Args:
            data: Frame data to verify
            
        Returns:
            True if data matches the simple pattern, False otherwise
        """
        for i in range(len(data)):
            if data[i] != i % 256:
                return False
        return True
    
    @staticmethod
    def generate_metadata(size: int) -> bytes:
        """
        Generate test metadata based on size
        
        Args:
            size: Size of the metadata in bytes
            
        Returns:
            Generated metadata as bytes
        """
        metadata = bytearray(size)
        for i in range(size):
            metadata[i] = i % 256
        return bytes(metadata)