#!/usr/bin/env bash
# Simple unit test runner for ZeroBuffer Python

set -e

echo "Running ZeroBuffer Python Unit Tests"
echo "===================================="

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

# Run unit tests
python -m pytest tests/test_zerobuffer.py -v --tb=short

echo "===================================="
echo "Unit tests completed"