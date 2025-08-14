#!/bin/bash
# Standard test script for zerobuffer C++ unit tests
# Usage: ./test.sh [unit|benchmark|all]

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Check if user is trying to run Harmony tests (but allow test numbers for local tests)
for arg in "$@"; do
    if [[ "$arg" == "cpp" ]] || [[ "$arg" == "csharp" ]] || [[ "$arg" == "python" ]]; then
        echo -e "${RED}=========================================${NC}"
        echo -e "${RED}ERROR: Wrong test script!${NC}"
        echo -e "${RED}=========================================${NC}"
        echo -e "${YELLOW}It looks like you're trying to run Harmony integration tests.${NC}"
        echo ""
        echo -e "${GREEN}To run Harmony tests, use:${NC}"
        echo -e "${GREEN}  cd ../  # Go to zerobuffer root directory${NC}"
        echo -e "${GREEN}  ./test.sh cpp         # Run all C++ Harmony tests${NC}"
        echo -e "${GREEN}  ./test.sh cpp 1.1     # Run specific test 1.1${NC}"
        echo -e "${GREEN}  ./test.sh csharp      # Run C# tests${NC}"
        echo -e "${GREEN}  ./test.sh python      # Run Python tests${NC}"
        echo ""
        echo -e "${YELLOW}Current directory: $(pwd)${NC}"
        echo -e "${YELLOW}You need to be in: $(dirname $(pwd))${NC}"
        exit 1
    fi
done

# Test type - default to unit tests if no argument provided
TEST_TYPE=${1:-unit}

# Check for test number format (e.g., 1.1, 1.2) or wildcard format (e.g., 1.*, 13.*)
if [[ "$TEST_TYPE" =~ ^[0-9]+\.[0-9*]+$ ]]; then
    # Running a specific test by number or pattern
    echo -e "${GREEN}=========================================${NC}"
    echo -e "${GREEN}Running Test(s) $TEST_TYPE${NC}"
    echo -e "${GREEN}=========================================${NC}"
    
    # Convert test number/pattern to Google Test filter
    # 1.1 -> Test_1_1_*
    # 1.* -> Test_1_*
    # 13.* -> Test_13_*
    if [[ "$TEST_TYPE" == *"*"* ]]; then
        # Wildcard pattern - remove the asterisk and convert dots
        TEST_PATTERN="${TEST_TYPE%\*}"  # Remove trailing asterisk
        TEST_FILTER="*Test_${TEST_PATTERN//./_}*"
    else
        # Specific test number
        TEST_FILTER="*Test_${TEST_TYPE//./_}_*"
    fi
    
    # Check if generated tests exist
    if [ ! -f "build/tests/generated/zerobuffer_generated_tests" ]; then
        echo -e "${YELLOW}Generated tests not built. Building now...${NC}"
        cd build && make -j8 zerobuffer_generated_tests && cd ..
    fi
    
    if [ -f "build/tests/generated/zerobuffer_generated_tests" ]; then
        echo -e "${YELLOW}Running test with filter: $TEST_FILTER${NC}"
        # Run test and capture exit code
        ./build/tests/generated/zerobuffer_generated_tests --gtest_filter="$TEST_FILTER" --gtest_color=yes
        TEST_RESULT=$?
        
        # Show test result summary
        if [ $TEST_RESULT -eq 0 ]; then
            echo -e "${GREEN}=========================================${NC}"
            echo -e "${GREEN}✓ Test(s) $TEST_TYPE PASSED${NC}"
            echo -e "${GREEN}=========================================${NC}"
        else
            echo -e "${RED}=========================================${NC}"
            echo -e "${RED}✗ Test(s) $TEST_TYPE FAILED${NC}"
            echo -e "${RED}=========================================${NC}"
        fi
        exit $TEST_RESULT
    else
        echo -e "${RED}Error: Generated tests not found!${NC}"
        echo -e "${YELLOW}Run the generator first:${NC}"
        echo -e "${YELLOW}  harmony-cpp-gen --input ../ZeroBuffer.Harmony.Tests/Features --output tests/generated${NC}"
        echo -e "${YELLOW}  Or run: ./generate_tests.sh${NC}"
        exit 1
    fi
fi

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}ZeroBuffer Test Suite${NC}"
echo -e "${GREEN}=========================================${NC}"

# Clean up any stale resources
echo -e "${YELLOW}Cleaning up stale resources...${NC}"
rm -f /dev/shm/zerobuffer-* 2>/dev/null || true
rm -f /dev/shm/sem.zerobuffer-* 2>/dev/null || true
rm -f /tmp/zerobuffer-*.lock 2>/dev/null || true

# Ensure build directory exists
if [ ! -d "build" ]; then
    echo -e "${YELLOW}Build directory not found. Building first...${NC}"
    ./build.sh
fi

cd build

# Run unit tests
if [ "$TEST_TYPE" == "unit" ] || [ "$TEST_TYPE" == "all" ]; then
    echo -e "${YELLOW}Running unit tests...${NC}"
    
    # Check if generated tests exist
    if [ ! -f "tests/generated/zerobuffer_generated_tests" ]; then
        echo -e "${YELLOW}Building generated tests...${NC}"
        make -j8 zerobuffer_generated_tests
    fi
    
    # Run generated tests
    if [ -f "tests/generated/zerobuffer_generated_tests" ]; then
        echo -e "${YELLOW}Running generated tests...${NC}"
        echo ""  # Add blank line for better visibility
        ./tests/generated/zerobuffer_generated_tests --gtest_color=yes
        TEST_RESULT=$?
        echo ""  # Add blank line after test output
        
        if [ $TEST_RESULT -eq 0 ]; then
            echo -e "${GREEN}✓ Generated tests completed successfully${NC}"
        else
            echo -e "${RED}✗ Some generated tests failed${NC}"
            exit $TEST_RESULT
        fi
    else
        echo -e "${RED}Error: Generated tests not found!${NC}"
        echo -e "${YELLOW}Run the generator first:${NC}"
        echo -e "${YELLOW}  cd .. && ./generate_tests.sh${NC}"
        exit 1
    fi
    
    echo -e "${GREEN}✓ All unit tests passed${NC}"
fi

# Run benchmarks
if [ "$TEST_TYPE" == "benchmark" ] || [ "$TEST_TYPE" == "all" ]; then
    echo -e "${YELLOW}Running benchmarks...${NC}"
    
    # Run actual benchmarks that exist
    for bench in benchmark_roundtrip benchmark_roundtrip_copy; do
        if [ -f "benchmarks/$bench" ]; then
            echo -e "${YELLOW}Running $bench...${NC}"
            ./benchmarks/$bench || true
        fi
    done
    echo -e "${GREEN}✓ Benchmarks complete${NC}"
fi

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Test run complete!${NC}"
echo -e "${GREEN}=========================================${NC}"