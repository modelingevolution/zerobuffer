#!/usr/bin/env bash
# Simple integration test runner for ZeroBuffer Python

set -e

echo "Running ZeroBuffer Python Integration Tests"
echo "=========================================="

# Check if virtual environment exists
if [ ! -d "venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv venv
fi

# Activate virtual environment
source venv/bin/activate

# Install dependencies if needed
if ! python -c "import pytest" 2>/dev/null; then
    echo "Installing test dependencies..."
    pip install -q pytest pytest-timeout posix-ipc
    pip install -q -e .
fi

# Clean up any stale shared memory resources
if [ -d "/tmp/zerobuffer" ]; then
    rm -rf /tmp/zerobuffer/test_* 2>/dev/null || true
fi

# Run integration tests
python -m pytest tests/test_scenarios.py -v --tb=short -k "not test_cross_process"

echo "=========================================="
echo "Integration tests completed"