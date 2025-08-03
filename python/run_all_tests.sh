#!/usr/bin/env bash
# Run all tests for ZeroBuffer Python

set -e

echo "Running All ZeroBuffer Python Tests"
echo "==================================="

# Run unit tests
./run_unit_tests.sh

echo ""

# Run integration tests
./run_integration_tests.sh

echo "==================================="
echo "All tests completed"