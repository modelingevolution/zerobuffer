#!/bin/bash

# Test script for running Harmony tests with platform filtering
# Usage: ./test.sh [-v] <platform> [test-number]
# Examples:
#   ./test.sh csharp         # Run all C#/C# tests
#   ./test.sh csharp 1.1     # Run specific test 1.1
#   ./test.sh csharp 4.3     # Run specific edge case test 4.3
#   ./test.sh -v csharp      # Run with verbose output (shows build)

set -e

# Check for verbose flag
VERBOSE=false
if [ "$1" = "-v" ]; then
    VERBOSE=true
    shift
fi

PLATFORM=$1
TEST_NUMBER=$2

if [ -z "$PLATFORM" ]; then
    echo "Usage: $0 [-v] <platform> [test-number]"
    echo "Available platforms: csharp, cpp, python"
    echo "Options:"
    echo "  -v            Show verbose output including build information"
    echo "Examples:"
    echo "  $0 csharp         # Run all C#/C# tests"
    echo "  $0 csharp 1.1     # Run specific test 1.1"
    echo "  $0 csharp 4.3     # Run specific edge case test 4.3"
    echo "  $0 -v csharp      # Run with verbose output"
    exit 1
fi

echo "Running tests for platform: $PLATFORM"

# Change to the test project directory
cd "$(dirname "$0")/ZeroBuffer.Harmony.Tests"

# Set test filter based on platform
case $PLATFORM in
    csharp)
        # Run only C#/C# tests
        BASE_FILTER="FullyQualifiedName~Csharp_Csharp"
        ;;
    cpp)
        # Run only C++/C++ tests
        BASE_FILTER="FullyQualifiedName~Cpp_Cpp"
        ;;
    python)
        # Run only Python/Python tests
        BASE_FILTER="FullyQualifiedName~Python_Python"
        ;;
    *)
        echo "Unknown platform: $PLATFORM"
        echo "Available platforms: csharp, cpp, python"
        exit 1
        ;;
esac

# If test number is provided, run only that specific test
if [ -n "$TEST_NUMBER" ]; then
    # Run specific test by display name
    FILTER="$BASE_FILTER&DisplayName~\"Test $TEST_NUMBER\""
    echo "Running specific test: $TEST_NUMBER"
else
    # Run all tests for the platform
    FILTER="$BASE_FILTER"
fi

# Set build verbosity based on verbose flag
if [ "$VERBOSE" = true ]; then
    BUILD_VERBOSITY="normal"
    echo "Running: dotnet test --filter \"$FILTER\" --logger \"console;verbosity=normal\" -- xunit.parallelExecution.disable=true"
    timeout 300 dotnet test --filter "$FILTER" --logger "console;verbosity=normal" -- xunit.parallelExecution.disable=true
else
    # Suppress build output but keep test output
    echo "Running tests (use -v for build details)..."
    
    # Build quietly first, suppressing all output except errors
    echo "Building..."
    if ! dotnet build --verbosity quiet --nologo > /dev/null 2>&1; then
        echo "Build failed! Run with -v to see details."
        exit 1
    fi
    
    # Run tests with no build, showing only test output
    timeout 300 dotnet test --no-build --filter "$FILTER" --logger "console;verbosity=normal" -- xunit.parallelExecution.disable=true
fi

exit_code=$?

if [ $exit_code -eq 124 ]; then
    echo "Tests timed out after 5 minutes"
    exit 1
elif [ $exit_code -ne 0 ]; then
    echo "Tests failed with exit code: $exit_code"
    exit $exit_code
fi

echo "Tests completed successfully"