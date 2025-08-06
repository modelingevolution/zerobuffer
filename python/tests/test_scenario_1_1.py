#!/usr/bin/env python3
"""
Test implementation for Scenario 1.1 - Simple Write-Read Cycle

This test demonstrates how pytest-bdd integrates with ZeroBuffer step definitions
to execute BDD scenarios from feature files.
"""

import sys
import os
from pathlib import Path
import pytest
import asyncio

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Import pytest-bdd
from pytest_bdd import scenario, given, when, then, parsers

# Import ZeroBuffer components
from zerobuffer import Reader, Writer, BufferConfig
from zerobuffer_serve.test_context import TestContext
from zerobuffer_serve.logging.dual_logger import DualLoggerProvider
from zerobuffer_serve.step_definitions.basic_communication import BasicCommunicationSteps


# Find the feature file - use local fixed copy
FEATURE_FILE = Path(__file__).parent.parent / "features" / "01-BasicCommunication.feature"
if not FEATURE_FILE.exists():
    # Fallback to original locations
    FEATURE_FILE = Path(__file__).parent.parent.parent / "ZeroBuffer.Harmony.Tests" / "Features" / "01-BasicCommunication.feature"
    if not FEATURE_FILE.exists():
        FEATURE_FILE = Path(__file__).parent.parent.parent / "csharp" / "ZeroBuffer.Tests" / "Features" / "01-BasicCommunication.feature"


# Define the scenario
@scenario(str(FEATURE_FILE), 'Test 1.1 - Simple Write-Read Cycle')
def test_simple_write_read_cycle():
    """Test 1.1 - Simple Write-Read Cycle"""
    pass


# Fixtures
@pytest.fixture
def test_context():
    """Provide test context"""
    context = TestContext()
    context.initialize(
        role="test",
        platform="python", 
        scenario="Test 1.1 - Simple Write-Read Cycle",
        test_run_id="pytest-bdd-1.1"
    )
    yield context
    context.cleanup()


@pytest.fixture
def logger():
    """Provide logger"""
    provider = DualLoggerProvider()
    return provider.get_logger("test_1_1")


@pytest.fixture
def steps(test_context, logger):
    """Provide step definitions instance"""
    return BasicCommunicationSteps(test_context, logger)


# Helper to run async functions
def run_async(coro):
    """Run async coroutine in sync context"""
    try:
        loop = asyncio.get_running_loop()
    except RuntimeError:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
    return loop.run_until_complete(coro)


# Step definitions - wrapping the class methods for pytest-bdd

# Background steps
@given("the test environment is initialized")
def test_environment_initialized(steps):
    """Initialize test environment"""
    steps.test_environment_initialized()


@given("all processes are ready")
def all_processes_ready(steps):
    """Confirm all processes are ready"""
    steps.all_processes_ready()


# Scenario steps
@given(parsers.re(r"the '(?P<process>[^']+)' process creates buffer '(?P<buffer_name>[^']+)' with metadata size '(?P<metadata_size>\d+)' and payload size '(?P<payload_size>\d+)'"))
def create_buffer(steps, process, buffer_name, metadata_size, payload_size):
    """Create a new ZeroBuffer with specified configuration"""
    return run_async(steps.create_buffer(process, buffer_name, metadata_size, payload_size))


@when(parsers.re(r"the '(?P<process>[^']+)' process connects to buffer '(?P<buffer_name>[^']+)'"))
def connect_to_buffer(steps, process, buffer_name):
    """Connect a writer to an existing buffer"""
    return run_async(steps.connect_to_buffer(process, buffer_name))


@when(parsers.re(r"the '(?P<process>[^']+)' process writes metadata with size '(?P<size>\d+)'"))
def write_metadata(steps, process, size):
    """Write metadata to the buffer"""
    return run_async(steps.write_metadata(process, size))


@when(parsers.re(r"the '(?P<process>[^']+)' process writes frame with size '(?P<size>\d+)' and sequence '(?P<sequence>\d+)'"))
def write_frame_with_sequence(steps, process, size, sequence):
    """Write a frame with specific size and sequence"""
    return run_async(steps.write_frame_with_sequence(process, size, sequence))


@then(parsers.re(r"the '(?P<process>[^']+)' process should read frame with sequence '(?P<sequence>\d+)' and size '(?P<size>\d+)'"))
def read_frame_verify_sequence_size(steps, process, sequence, size):
    """Read and verify frame with sequence and size"""
    return run_async(steps.read_frame_verify_sequence_size(process, sequence, size))


@then(parsers.re(r"the '(?P<process>[^']+)' process should validate frame data"))
def validate_frame_data(steps, process):
    """Validate frame data"""
    return run_async(steps.validate_frame_data(process))


@then(parsers.re(r"the '(?P<process>[^']+)' process signals space available"))
def signal_space_available(steps, process):
    """Signal that space is available"""
    return run_async(steps.signal_space_available(process))


if __name__ == "__main__":
    # Run this specific test
    print(f"Feature file: {FEATURE_FILE}")
    print(f"Feature file exists: {FEATURE_FILE.exists()}")
    
    # Run the test
    pytest.main([__file__, "-v", "-s"])