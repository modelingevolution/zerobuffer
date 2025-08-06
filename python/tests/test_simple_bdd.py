#!/usr/bin/env python3
"""
Simple test to verify pytest-bdd integration works correctly.

This is a minimal example to test the architecture before full implementation.
"""

import pytest
from pathlib import Path

# Try to import pytest-bdd
try:
    from pytest_bdd import scenario, given, when, then, parsers
    PYTEST_BDD_AVAILABLE = True
except ImportError:
    PYTEST_BDD_AVAILABLE = False
    # Skip all tests in this module if pytest-bdd is not installed
    pytestmark = pytest.mark.skip(reason="pytest-bdd not installed")


def test_pytest_bdd_available():
    """Test that pytest-bdd is available"""
    assert PYTEST_BDD_AVAILABLE, "pytest-bdd should be installed for BDD tests"


if PYTEST_BDD_AVAILABLE:
    # Create a simple inline feature for testing
    SIMPLE_FEATURE = """
    Feature: Simple Test
        As a developer
        I want to verify pytest-bdd integration
        So that I can use BDD tests with ZeroBuffer
        
        Scenario: Basic buffer creation
            Given the test environment is initialized
            When a buffer named 'test' is created with size 1024
            Then the buffer 'test' should exist
            And the buffer 'test' should have size 1024
    """
    
    # Write the feature to a temp file for pytest-bdd to read
    import tempfile
    with tempfile.NamedTemporaryFile(mode='w', suffix='.feature', delete=False) as f:
        f.write(SIMPLE_FEATURE)
        temp_feature_path = f.name
    
    # Define the scenario
    @scenario(temp_feature_path, 'Basic buffer creation')
    def test_basic_buffer_creation():
        """Test basic buffer creation scenario"""
        pass
    
    # Step definitions
    @given("the test environment is initialized")
    def init_environment():
        """Initialize test environment"""
        return {"initialized": True}
    
    @when(parsers.parse("a buffer named '{name}' is created with size {size:d}"))
    def create_buffer(name, size):
        """Create a buffer"""
        # Simulate buffer creation
        buffer = {"name": name, "size": size}
        return buffer
    
    @then(parsers.parse("the buffer '{name}' should exist"))
    def verify_buffer_exists(name, create_buffer):
        """Verify buffer exists"""
        assert create_buffer["name"] == name
    
    @then(parsers.parse("the buffer '{name}' should have size {size:d}"))
    def verify_buffer_size(name, size, create_buffer):
        """Verify buffer size"""
        assert create_buffer["name"] == name
        assert create_buffer["size"] == size


def test_feature_files_exist():
    """Test that feature files exist in expected location"""
    possible_paths = [
        Path(__file__).parent.parent.parent / "ZeroBuffer.Harmony.Tests" / "Features",
        Path(__file__).parent.parent.parent / "csharp" / "ZeroBuffer.Tests" / "Features",
        Path(__file__).parent.parent.parent.parent / "csharp" / "ZeroBuffer.Tests" / "Features",
    ]
    
    feature_dir_found = False
    for path in possible_paths:
        if path.exists():
            feature_dir_found = True
            print(f"Found feature directory: {path}")
            feature_files = list(path.glob("*.feature"))
            assert len(feature_files) > 0, f"No feature files found in {path}"
            print(f"Found {len(feature_files)} feature files")
            break
    
    if not feature_dir_found:
        pytest.skip("Feature directory not found")


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])