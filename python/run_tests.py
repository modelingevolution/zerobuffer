#!/usr/bin/env python3
"""
Test runner for ZeroBuffer Python implementation
"""

import sys
import os
import subprocess
import multiprocessing

def main():
    """Run all tests"""
    # Ensure we're in the right directory
    script_dir = os.path.dirname(os.path.abspath(__file__))
    os.chdir(script_dir)
    
    # Set up Python path
    sys.path.insert(0, script_dir)
    
    # For multiprocessing tests
    multiprocessing.set_start_method('spawn', force=True)
    
    print("Running ZeroBuffer Python Tests")
    print("=" * 50)
    
    # Check if pytest is installed
    try:
        import pytest
    except ImportError:
        print("ERROR: pytest not installed")
        print("Install with: pip install pytest pytest-timeout pytest-cov")
        return 1
    
    # Run unit tests
    print("\n1. Running Unit Tests")
    print("-" * 30)
    result = subprocess.run([
        sys.executable, "-m", "pytest",
        "tests/test_zerobuffer.py",
        "-v",
        "--tb=short"
    ])
    
    if result.returncode != 0:
        print("\nUnit tests failed!")
        return result.returncode
    
    # Run scenario tests
    print("\n2. Running Integration Scenario Tests")
    print("-" * 30)
    result = subprocess.run([
        sys.executable, "-m", "pytest",
        "tests/test_scenarios.py",
        "-v",
        "--tb=short",
        "-k", "not test_cross_process"  # Skip multiprocess tests in CI
    ])
    
    if result.returncode != 0:
        print("\nScenario tests failed!")
        return result.returncode
    
    print("\n" + "=" * 50)
    print("All tests passed!")
    return 0

if __name__ == "__main__":
    sys.exit(main())