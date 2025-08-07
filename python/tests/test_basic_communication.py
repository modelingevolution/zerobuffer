#!/usr/bin/env python3
"""
Python-only tests for Basic Communication scenarios

These tests implement the exact same scenarios as defined in BasicCommunication.feature
and should work identically when invoked by Harmony.
"""

import asyncio
import sys
import os
import pytest

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from zerobuffer_serve.test_context import TestContext
from zerobuffer_serve.logging.dual_logger import DualLoggerProvider
from zerobuffer_serve.step_definitions import BasicCommunicationSteps


class TestBasicCommunication:
    """Tests for Basic Communication scenarios"""
    
    def setup_method(self):
        """Setup for each test"""
        self.logger_provider = DualLoggerProvider()
        self.test_context = TestContext()
        self.test_context.initialize(
            role="test",  # Not used anymore, but needed for initialization
            platform="python",
            scenario="Basic Communication Test",
            test_run_id="python-only-test"
        )
        self.steps = BasicCommunicationSteps(
            self.test_context, 
            self.logger_provider.get_logger("BasicCommunicationSteps")
        )
        
    def teardown_method(self):
        """Cleanup after each test"""
        try:
            # Clean up any readers and writers from the steps
            if hasattr(self.steps, '_readers'):
                for reader in self.steps._readers.values():
                    try:
                        reader.cleanup()
                    except:
                        pass
                        
            if hasattr(self.steps, '_writers'):
                for writer in self.steps._writers.values():
                    try:
                        writer.cleanup()
                    except:
                        pass
                        
            self.test_context.cleanup()
        except:
            pass
        
    @pytest.mark.asyncio
    async def test_1_1_simple_write_read_cycle(self):
        """Test 1.1 - Simple Write-Read Cycle"""
        print("\n=== Test 1.1 - Simple Write-Read Cycle ===")
        
        # Background
        self.steps.test_environment_initialized()
        self.steps.all_processes_ready()
        
        # Given the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'
        await self.steps.create_buffer("reader", "test-basic", "1024", "10240")
        
        # When the 'writer' process connects to buffer 'test-basic'
        await self.steps.connect_to_buffer("writer", "test-basic")
        
        # And the 'writer' process writes metadata with size '100'
        await self.steps.write_metadata("writer", "100")
        
        # And the 'writer' process writes frame with size '1024' and sequence '1'
        await self.steps.write_frame_with_sequence("writer", "1024", "1")
        
        # Then the 'reader' process should read frame with sequence '1' and size '1024'
        await self.steps.read_frame_verify_sequence_size("reader", "1", "1024")
        
        # And the 'reader' process should validate frame data
        await self.steps.validate_frame_data("reader")
        
        # And the 'reader' process signals space available
        await self.steps.signal_space_available("reader")
        
        print("✅ Test 1.1 completed successfully!")
        
    @pytest.mark.asyncio
    async def test_1_2_multiple_frames_sequential(self):
        """Test 1.2 - Multiple Frames Sequential"""
        print("\n=== Test 1.2 - Multiple Frames Sequential ===")
        
        # Background
        self.steps.test_environment_initialized()
        self.steps.all_processes_ready()
        
        # Given the 'reader' process creates buffer 'test-multi' with metadata size '1024' and payload size '102400'
        await self.steps.create_buffer("reader", "test-multi", "1024", "102400")
        
        # When the 'writer' process connects to buffer 'test-multi'
        await self.steps.connect_to_buffer("writer", "test-multi")
        
        # And the 'writer' process writes frame with sequence '1'
        await self.steps.write_frame_sequence_only("writer", "1")
        
        # And the 'writer' process writes frame with sequence '2'
        await self.steps.write_frame_sequence_only("writer", "2")
        
        # And the 'writer' process writes frame with sequence '3'
        await self.steps.write_frame_sequence_only("writer", "3")
        
        # Then the 'reader' process should read frame with sequence '1'
        await self.steps.read_frame_verify_sequence("reader", "1")
        
        # And the 'reader' process signals space available
        await self.steps.signal_space_available("reader")
        
        # And the 'reader' process should read frame with sequence '2'
        await self.steps.read_frame_verify_sequence("reader", "2")
        
        # And the 'reader' process signals space available
        await self.steps.signal_space_available("reader")
        
        # And the 'reader' process should read frame with sequence '3'
        await self.steps.read_frame_verify_sequence("reader", "3")
        
        # And the 'reader' process should verify all frames maintain sequential order
        await self.steps.verify_sequential_order("reader")
        
        print("✅ Test 1.2 completed successfully!")
        
    @pytest.mark.asyncio
    async def test_1_3_buffer_full_handling(self):
        """Test 1.3 - Buffer Full Handling"""
        print("\n=== Test 1.3 - Buffer Full Handling ===")
        
        # Background
        self.steps.test_environment_initialized()
        self.steps.all_processes_ready()
        
        # Given the 'reader' process creates buffer 'test-full' with metadata size '1024' and payload size '10240'
        await self.steps.create_buffer("reader", "test-full", "1024", "10240")
        
        # When the 'writer' process connects to buffer 'test-full'
        await self.steps.connect_to_buffer("writer", "test-full")
        
        # And the 'writer' process writes frames until buffer is full
        await self.steps.write_until_full("writer")
        
        # Then the 'writer' process should experience timeout or buffer full on next write
        await self.steps.verify_buffer_full("writer")
        
        # When the 'reader' process reads one frame
        await self.steps.read_one_frame("reader")
        
        # And the 'reader' process signals space available
        await self.steps.signal_space_available("reader")
        
        # Then the 'writer' process should write successfully immediately
        await self.steps.verify_write_succeeds("writer")
        
        print("✅ Test 1.3 completed successfully!")
        
    @pytest.mark.asyncio
    async def test_1_4_zero_copy_write_operations(self):
        """Test 1.4 - Zero-Copy Write Operations"""
        print("\n=== Test 1.4 - Zero-Copy Write Operations ===")
        
        # Background
        self.steps.test_environment_initialized()
        self.steps.all_processes_ready()
        
        # Given the 'reader' process creates buffer 'test-zerocopy' with metadata size '1024' and payload size '102400'
        await self.steps.create_buffer("reader", "test-zerocopy", "1024", "102400")
        
        # When the 'writer' process connects to buffer 'test-zerocopy'
        await self.steps.connect_to_buffer("writer", "test-zerocopy")
        
        # And the 'writer' process requests zero-copy frame of size '4096'
        await self.steps.request_zero_copy_frame("writer", "4096")
        
        # And the 'writer' process fills zero-copy buffer with test pattern
        await self.steps.fill_zero_copy_buffer("writer")
        
        # And the 'writer' process commits zero-copy frame
        await self.steps.commit_zero_copy_frame("writer")
        
        # Then the 'reader' process should read frame with size '4096'
        await self.steps.read_frame_verify_size("reader", "4096")
        
        # And the 'reader' process should verify frame data matches test pattern
        await self.steps.verify_test_pattern("reader")
        
        print("✅ Test 1.4 completed successfully!")
        
    @pytest.mark.asyncio
    async def test_1_5_mixed_frame_sizes(self):
        """Test 1.5 - Mixed Frame Sizes"""
        print("\n=== Test 1.5 - Mixed Frame Sizes ===")
        
        # Background
        self.steps.test_environment_initialized()
        self.steps.all_processes_ready()
        
        # Given the 'reader' process creates buffer 'test-mixed' with metadata size '1024' and payload size '102400'
        await self.steps.create_buffer("reader", "test-mixed", "1024", "102400")
        
        # When the 'writer' process connects to buffer 'test-mixed'
        await self.steps.connect_to_buffer("writer", "test-mixed")
        
        # And the 'writer' process writes frame with size '100'
        await self.steps.write_frame_with_size("writer", "100")
        
        # And the 'writer' process writes frame with size '1024'
        await self.steps.write_frame_with_size("writer", "1024")
        
        # And the 'writer' process writes frame with size '10240'
        await self.steps.write_frame_with_size("writer", "10240")
        
        # And the 'writer' process writes frame with size '1'
        await self.steps.write_frame_with_size("writer", "1")
        
        # Then the 'reader' process should read 4 frames with sizes '100,1024,10240,1' in order
        await self.steps.read_frames_verify_sizes("reader", "4", "100,1024,10240,1")
        
        print("✅ Test 1.5 completed successfully!")
        


if __name__ == "__main__":
    # Run tests directly
    pytest.main([__file__, "-v", "-s"])