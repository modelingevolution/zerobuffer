#!/usr/bin/env python3
"""
Test runner that executes tests directly from Gherkin feature files using pytest-bdd.

This ensures Python tests stay in sync with the feature files (source of truth).
"""

import sys
import os
from pathlib import Path
import pytest

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Check if pytest-bdd is available
try:
    from pytest_bdd import scenarios, given, when, then, parsers
except ImportError:
    pytest.skip("pytest-bdd is not installed. Run: pip install pytest-bdd", allow_module_level=True)

from zerobuffer_serve.test_context import TestContext
from zerobuffer_serve.logging.dual_logger import DualLoggerProvider
from zerobuffer_serve.step_definitions import (
    BasicCommunicationSteps,
    EdgeCasesSteps,
    ErrorHandlingSteps,
    ProcessLifecycleSteps,
    SynchronizationSteps,
)


# Find feature files
def get_feature_dir():
    """Find the feature files directory"""
    # First check for local fixed features (preferred)
    local_features = Path(__file__).parent.parent / "features"
    if local_features.exists():
        return local_features
    
    # Fallback to original locations
    possible_paths = [
        Path(__file__).parent.parent.parent / "ZeroBuffer.Harmony.Tests" / "Features",
        Path(__file__).parent.parent.parent / "csharp" / "ZeroBuffer.Tests" / "Features",
        Path(__file__).parent.parent.parent.parent / "csharp" / "ZeroBuffer.Tests" / "Features",
    ]
    
    for path in possible_paths:
        if path.exists():
            return path
    
    # If not found, return a path that will cause a clear error
    return local_features


FEATURE_DIR = get_feature_dir()


# Load ALL feature files to get all 75 tests
if FEATURE_DIR.exists():
    # Load all feature files
    for feature_file in sorted(FEATURE_DIR.glob("*.feature")):
        try:
            scenarios(str(feature_file))
            print(f"Loaded: {feature_file.name}")
        except Exception as e:
            print(f"Warning: Could not load {feature_file.name}: {e}")


# Fixtures for step classes
@pytest.fixture
def test_context():
    """Provide a test context for each test"""
    context = TestContext()
    context.initialize(
        role="test",
        platform="python",
        scenario="pytest-bdd test",
        test_run_id="pytest-bdd-run"
    )
    yield context
    context.cleanup()


@pytest.fixture
def logger():
    """Provide a logger for tests"""
    provider = DualLoggerProvider()
    return provider.get_logger("pytest-bdd")


@pytest.fixture
def basic_steps(test_context, logger):
    """Provide BasicCommunicationSteps instance"""
    return BasicCommunicationSteps(test_context, logger)


@pytest.fixture
def edge_steps(test_context, logger):
    """Provide EdgeCasesSteps instance"""
    return EdgeCasesSteps(test_context, logger)


@pytest.fixture
def error_steps(test_context, logger):
    """Provide ErrorHandlingSteps instance"""
    return ErrorHandlingSteps(test_context, logger)


# Step definitions that bridge pytest-bdd to our step classes
# We need to wrap our class methods to work with pytest-bdd

# Background steps
@given("the test environment is initialized")
def test_environment_initialized(basic_steps):
    """Initialize test environment"""
    basic_steps.test_environment_initialized()


@given("all processes are ready")
def all_processes_ready(basic_steps):
    """Confirm all processes are ready"""
    basic_steps.all_processes_ready()


# Buffer creation
@given(parsers.re(r"the '(?P<process>[^']+)' process creates buffer '(?P<buffer_name>[^']+)' with metadata size '(?P<metadata_size>\d+)' and payload size '(?P<payload_size>\d+)'"))
def create_buffer(basic_steps, process, buffer_name, metadata_size, payload_size):
    """Create a new ZeroBuffer with specified configuration"""
    import asyncio
    # Run async method in sync context
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.create_buffer(process, buffer_name, metadata_size, payload_size)
    )


# Writer connection
@when(parsers.re(r"the '(?P<process>[^']+)' process connects to buffer '(?P<buffer_name>[^']+)'"))
def connect_to_buffer(basic_steps, process, buffer_name):
    """Connect a writer to an existing buffer"""
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.connect_to_buffer(process, buffer_name)
    )


# Metadata operations
@when(parsers.re(r"the '(?P<process>[^']+)' process writes metadata with size '(?P<size>\d+)'"))
def write_metadata(basic_steps, process, size):
    """Write metadata to the buffer"""
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.write_metadata(process, size)
    )


# Frame operations
@when(parsers.re(r"the '(?P<process>[^']+)' process writes frame with size '(?P<size>\d+)' and sequence '(?P<sequence>\d+)'"))
def write_frame_with_sequence(basic_steps, process, size, sequence):
    """Write a frame with specific size and sequence"""
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.write_frame_with_sequence(process, size, sequence)
    )


@then(parsers.re(r"the '(?P<process>[^']+)' process should read frame with sequence '(?P<sequence>\d+)' and size '(?P<size>\d+)'"))
def read_frame_verify_sequence_size(basic_steps, process, sequence, size):
    """Read and verify frame with sequence and size"""
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.read_frame_verify_sequence_size(process, sequence, size)
    )


@then(parsers.re(r"the '(?P<process>[^']+)' process should validate frame data"))
def validate_frame_data(basic_steps, process):
    """Validate frame data"""
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.validate_frame_data(process)
    )


@then(parsers.re(r"the '(?P<process>[^']+)' process signals space available"))
def signal_space_available(basic_steps, process):
    """Signal that space is available"""
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(
        basic_steps.signal_space_available(process)
    )


# Helper for running async methods
def run_async(coro):
    """Helper to run async coroutines in sync context"""
    import asyncio
    try:
        loop = asyncio.get_running_loop()
    except RuntimeError:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
    return loop.run_until_complete(coro)


if __name__ == "__main__":
    # Run with: python -m pytest tests/test_features_bdd.py -v -s
    print(f"Feature directory: {FEATURE_DIR}")
    print(f"Feature files exist: {FEATURE_DIR.exists()}")
    if FEATURE_DIR.exists():
        print(f"Feature files found: {list(FEATURE_DIR.glob('*.feature'))}")
    
    pytest.main([__file__, "-v", "-s"])