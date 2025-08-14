#!/bin/bash

# Test script for running Harmony tests with platform filtering
# Usage: ./test.sh [-v] [platform] [test-number]
# Examples:
#   ./test.sh csharp         # Run all C#/C# tests
#   ./test.sh cpp_csharp     # Run all C++ reader / C# writer tests
#   ./test.sh csharp_cpp 1.1 # Run test 1.1 for C# reader / C++ writer
#   ./test.sh 1.1            # Run test 1.1 for all platform combinations
#   ./test.sh cpp "13.*"     # Run all test 13.x for C++/C++
#   ./test.sh "1.*"          # Run all test 1.x for all platforms
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

# Check if first argument looks like a test number (e.g., 1.1, 4.3) or wildcard (e.g., 1.*, 13.*)
if [[ "$PLATFORM" =~ ^[0-9]+\.[0-9*]+$ ]]; then
    # First argument is a test number or pattern, run for all platforms
    TEST_NUMBER=$PLATFORM
    PLATFORM="all"
fi

if [ -z "$PLATFORM" ]; then
    echo "Usage: $0 [-v] [platform] [test-number]"
    echo "       $0 [-v] [test-number]  # Run test for all platform combinations"
    echo "Available platforms:"
    echo "  Single: csharp, cpp, python"
    echo "  Cross:  cpp_csharp, csharp_cpp, cpp_python, python_cpp, csharp_python, python_csharp"
    echo "  All:    all"
    echo "Options:"
    echo "  -v            Show verbose test execution output"
    echo "Examples:"
    echo "  $0 csharp         # Run all C#/C# tests"
    echo "  $0 cpp_csharp     # Run all C++ reader / C# writer tests"
    echo "  $0 csharp_cpp 1.1 # Run test 1.1 for C# reader / C++ writer"
    echo "  $0 1.1            # Run test 1.1 for all platform combinations"
    echo "  $0 cpp \"13.*\"     # Run all test 13.x for C++/C++"
    echo "  $0 \"1.*\"          # Run all test 1.x for all platforms"
    echo "  $0 -v csharp      # Run with verbose output"
    exit 1
fi

# Change to the test project directory
cd "$(dirname "$0")/ZeroBuffer.Harmony.Tests"

# Set test filter based on platform
case $PLATFORM in
    csharp)
        echo "Running tests for platform: C#/C#"
        # Run only C#/C# tests
        BASE_FILTER="FullyQualifiedName~Csharp_Csharp"
        ;;
    cpp)
        echo "Running tests for platform: C++/C++"
        # Run only C++/C++ tests
        BASE_FILTER="FullyQualifiedName~Cpp_Cpp"
        ;;
    python)
        echo "Running tests for platform: Python/Python"
        # Run only Python/Python tests
        BASE_FILTER="FullyQualifiedName~Python_Python"
        ;;
    cpp_csharp|cpp-csharp)
        echo "Running tests for platform: C++ reader / C# writer"
        # Run C++ reader with C# writer tests
        BASE_FILTER="FullyQualifiedName~Cpp_Csharp"
        ;;
    csharp_cpp|csharp-cpp)
        echo "Running tests for platform: C# reader / C++ writer"
        # Run C# reader with C++ writer tests
        BASE_FILTER="FullyQualifiedName~Csharp_Cpp"
        ;;
    cpp_python|cpp-python)
        echo "Running tests for platform: C++ reader / Python writer"
        # Run C++ reader with Python writer tests
        BASE_FILTER="FullyQualifiedName~Cpp_Python"
        ;;
    python_cpp|python-cpp)
        echo "Running tests for platform: Python reader / C++ writer"
        # Run Python reader with C++ writer tests
        BASE_FILTER="FullyQualifiedName~Python_Cpp"
        ;;
    csharp_python|csharp-python)
        echo "Running tests for platform: C# reader / Python writer"
        # Run C# reader with Python writer tests
        BASE_FILTER="FullyQualifiedName~Csharp_Python"
        ;;
    python_csharp|python-csharp)
        echo "Running tests for platform: Python reader / C# writer"
        # Run Python reader with C# writer tests
        BASE_FILTER="FullyQualifiedName~Python_Csharp"
        ;;
    all)
        if [ -n "$TEST_NUMBER" ]; then
            echo "Running test $TEST_NUMBER for all platform combinations"
        else
            echo "Running all tests for all platform combinations"
        fi
        # No platform filter - run tests for all platforms
        BASE_FILTER=""
        ;;
    *)
        echo "Unknown platform: $PLATFORM"
        echo "Available platforms:"
        echo "  Single: csharp, cpp, python"
        echo "  Cross:  cpp_csharp, csharp_cpp, cpp_python, python_cpp, csharp_python, python_csharp"
        echo "  All:    all"
        exit 1
        ;;
esac

# If test number is provided, run only that specific test or pattern
if [ -n "$TEST_NUMBER" ]; then
    # Check if it's a wildcard pattern
    if [[ "$TEST_NUMBER" == *"*"* ]]; then
        # Wildcard pattern - remove the asterisk for broader match
        TEST_PATTERN="${TEST_NUMBER%\*}"  # Remove trailing asterisk
        if [ -n "$BASE_FILTER" ]; then
            FILTER="$BASE_FILTER&DisplayName~\"Test $TEST_PATTERN\""
        else
            FILTER="DisplayName~\"Test $TEST_PATTERN\""
        fi
        echo "Running tests matching pattern: $TEST_NUMBER"
    else
        # Specific test - add " -" after test number to ensure exact match
        # (e.g., "Test 13.1 -" won't match "Test 13.10 -")
        if [ -n "$BASE_FILTER" ]; then
            FILTER="$BASE_FILTER&DisplayName~\"Test $TEST_NUMBER -\""
        else
            FILTER="DisplayName~\"Test $TEST_NUMBER -\""
        fi
        echo "Running specific test: $TEST_NUMBER"
    fi
else
    # Run all tests for the platform
    if [ -n "$BASE_FILTER" ]; then
        FILTER="$BASE_FILTER"
    else
        # No filter means run all tests
        FILTER=""
    fi
fi

# Run tests with appropriate verbosity
if [ "$VERBOSE" = true ]; then
    if [ -n "$FILTER" ]; then
        echo "Running: dotnet test ZeroBuffer.Harmony.Tests.csproj --filter \"$FILTER\" --logger \"console;verbosity=detailed\" -- xunit.parallelExecution.disable=true"
        FILTER_ARG="--filter \"$FILTER\""
    else
        echo "Running: dotnet test ZeroBuffer.Harmony.Tests.csproj --logger \"console;verbosity=detailed\" -- xunit.parallelExecution.disable=true"
        FILTER_ARG=""
    fi
    
    # Run tests (will build automatically if needed)
    # Filter out xUnit discovery/start/finish messages and SpecFlow generation messages
    # Use set -o pipefail to preserve exit code through the pipe
    set -o pipefail
    if [ -n "$FILTER" ]; then
        timeout 300 dotnet test ZeroBuffer.Harmony.Tests.csproj --filter "$FILTER" --logger "console;verbosity=detailed" --verbosity minimal -- xunit.parallelExecution.disable=true 2>&1 | \
            grep -v "^\[xUnit\.net.*Discovering:" | \
            grep -v "^\[xUnit\.net.*Discovered:" | \
            grep -v "^\[xUnit\.net.*Starting:" | \
            grep -v "^\[xUnit\.net.*Finished:" | \
            grep -v "^  SpecFlowGeneratedFiles:" | \
            grep -v "^  SpecFlowFeatureFiles:"
    else
        timeout 300 dotnet test ZeroBuffer.Harmony.Tests.csproj --logger "console;verbosity=minimal" --verbosity minimal -- xunit.parallelExecution.disable=true 2>&1 | \
            grep -v "^\[xUnit\.net.*Discovering:" | \
            grep -v "^\[xUnit\.net.*Discovered:" | \
            grep -v "^\[xUnit\.net.*Starting:" | \
            grep -v "^\[xUnit\.net.*Finished:" | \
            grep -v "^  SpecFlowGeneratedFiles:" | \
            grep -v "^  SpecFlowFeatureFiles:"
    fi
    exit_code=$?
    set +o pipefail
else
    echo "Running tests..."
    
    # Run tests with minimal build output and minimal test output
    # Filter out xUnit discovery/start/finish messages, SpecFlow generation messages, and build output
    # Use set -o pipefail to preserve exit code through the pipe
    set -o pipefail
    if [ -n "$FILTER" ]; then
        timeout 300 dotnet test ZeroBuffer.Harmony.Tests.csproj --filter "$FILTER" --logger "console;verbosity=minimal" --verbosity minimal -- xunit.parallelExecution.disable=true 2>&1 | \
            grep -v "^\[xUnit\.net.*Discovering:" | \
            grep -v "^\[xUnit\.net.*Discovered:" | \
            grep -v "^\[xUnit\.net.*Starting:" | \
            grep -v "^\[xUnit\.net.*Finished:" | \
            grep -v "^  SpecFlowGeneratedFiles:" | \
            grep -v "^  SpecFlowFeatureFiles:" | \
            grep -v "^  Determining projects to restore" | \
            grep -v "^  Restored " | \
            grep -v "^  All projects are up-to-date" | \
            grep -v " -> " | \
            grep -v "^  Copying feature files"
    else
        timeout 300 dotnet test ZeroBuffer.Harmony.Tests.csproj --logger "console;verbosity=minimal" --verbosity minimal -- xunit.parallelExecution.disable=true 2>&1 | \
            grep -v "^\[xUnit\.net.*Discovering:" | \
            grep -v "^\[xUnit\.net.*Discovered:" | \
            grep -v "^\[xUnit\.net.*Starting:" | \
            grep -v "^\[xUnit\.net.*Finished:" | \
            grep -v "^  SpecFlowGeneratedFiles:" | \
            grep -v "^  SpecFlowFeatureFiles:" | \
            grep -v "^  Determining projects to restore" | \
            grep -v "^  Restored " | \
            grep -v "^  All projects are up-to-date" | \
            grep -v " -> " | \
            grep -v "^  Copying feature files"
    fi
    exit_code=$?
    set +o pipefail
fi

if [ $exit_code -eq 124 ]; then
    echo "Tests timed out after 5 minutes"
    exit 1
elif [ $exit_code -ne 0 ]; then
    echo "Tests failed with exit code: $exit_code"
    exit $exit_code
fi

echo "Tests completed successfully"