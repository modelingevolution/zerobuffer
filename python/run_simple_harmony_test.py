#!/usr/bin/env python3
"""
Run a simple Harmony test with both reader and writer in same process
This tests the step implementations without the complexity of multiple processes
"""

import asyncio
import sys
import os

# Add parent directory to path to import zerobuffer_serve modules
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from zerobuffer_serve.test_context import TestContext
from zerobuffer_serve.logging.dual_logger import DualLoggerProvider
from zerobuffer_serve.step_definitions import BasicCommunicationSteps


async def run_simple_test():
    """Run a simple test scenario"""
    print("=== Running Simple Harmony Test ===\n")
    
    # Create test context and logger
    logger_provider = DualLoggerProvider()
    test_context = TestContext()
    
    # Initialize test context
    test_context.initialize(
        role="reader",
        platform="python",
        scenario="Simple Write-Read Cycle",
        test_run_id="simple-test"
    )
    
    # Create step instance
    steps = BasicCommunicationSteps(test_context, logger_provider.get_logger("BasicCommunicationSteps"))
    
    # Test scenario: Simple Write-Read Cycle
    print("Scenario: Simple Write-Read Cycle\n")
    
    try:
        # Background steps
        print("Background:")
        steps.test_environment_initialized()
        print("  ✓ Given the test environment is initialized")
        
        steps.all_processes_ready()
        print("  ✓ Given all processes are ready")
        
        # Test steps
        print("\nSteps:")
        
        # Reader creates buffer
        await steps.create_buffer("reader", "test-simple", "1024", "10240")
        print("  ✓ Given the 'reader' process creates buffer 'test-simple' with metadata size '1024' and payload size '10240'")
        
        # Writer connects - switch context to writer role
        test_context._role = "writer"  # Direct access for testing
        await steps.connect_to_buffer("writer", "test-simple")
        print("  ✓ When the 'writer' process connects to buffer 'test-simple'")
        
        # Writer writes metadata
        await steps.write_metadata("writer", "100")
        print("  ✓ When the 'writer' process writes metadata with size '100'")
        
        # Writer writes frame
        await steps.write_frame_with_sequence("writer", "1024", "1")
        print("  ✓ When the 'writer' process writes frame with size '1024' and sequence '1'")
        
        # Reader reads frame - switch back to reader role
        test_context._role = "reader"  # Direct access for testing
        await steps.read_frame_verify_sequence_size("reader", "1", "1024")
        print("  ✓ Then the 'reader' process should read frame with sequence '1' and size '1024'")
        
        # Reader validates frame
        await steps.validate_frame_data("reader")
        print("  ✓ Then the 'reader' process should validate frame data")
        
        # Reader signals space available
        await steps.signal_space_available("reader")
        print("  ✓ Then the 'reader' process signals space available")
        
        print("\n✅ Test completed successfully!")
        
    except Exception as e:
        print(f"\n❌ Test failed: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
    finally:
        # Clean up
        test_context.cleanup()


if __name__ == "__main__":
    asyncio.run(run_simple_test())