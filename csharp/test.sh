#!/bin/bash

# Test runner script for ZeroBuffer C# in-process tests

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default values
FILTER=""
VERBOSITY="minimal"
NO_BUILD=""
SPECIFIC_TEST=""

# Function to display usage
usage() {
    echo "Usage: $0 [options] [test-name]"
    echo "Options:"
    echo "  -f, --filter <pattern>    Filter tests by pattern (e.g., BasicCommunication)"
    echo "  -v, --verbose             Use detailed verbosity"
    echo "  -n, --no-build           Skip building the project"
    echo "  -h, --help               Display this help message"
    echo ""
    echo "Examples:"
    echo "  $0                       # Run all tests"
    echo "  $0 BasicCommunication    # Run BasicCommunication tests"
    echo "  $0 -v EdgeCases         # Run EdgeCases tests with verbose output"
    echo "  $0 -n                   # Run all tests without building"
    echo "  $0 edge                 # Run all edge case tests one by one"
    echo "  $0 4.3                 # Run specific test 4.3"
    echo "  $0 5.5 -v               # Run test 5.5 with verbose output"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -f|--filter)
            FILTER="$2"
            shift 2
            ;;
        -v|--verbose)
            VERBOSITY="normal"
            shift
            ;;
        -n|--no-build)
            NO_BUILD="--no-build"
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        edge)
            # Special handling for edge case tests - run them one by one
            echo -e "${YELLOW}Running edge case tests one by one...${NC}"
            TESTS=(
                "Test4_3"  # Edge case - Zero-Sized Metadata Block
                "Test4_4"  # Edge case - Minimum Buffer Sizes
                "Test5_5"  # Corruption Detection - Wrap-Around With Wasted Space
                "Test5_6"  # Corruption Detection - Continuous Free Space Calculation
                "Test5_7"  # Corruption Detection - Maximum Frame Size
                "Test4_5"  # Edge case - Reader Slower Than Writer
                "Test12_1" # Protocol Compliance - OIEB
                "Test12_2" # Protocol Compliance - Memory Alignment
            )
            
            for test in "${TESTS[@]}"; do
                echo -e "\n${YELLOW}Running: $test${NC}"
                if dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~$test" -v $VERBOSITY $NO_BUILD; then
                    echo -e "${GREEN}✓ $test passed${NC}"
                else
                    echo -e "${RED}✗ $test failed${NC}"
                fi
            done
            exit 0
            ;;
        4.3|4_3)
            # Run specific test 4.3
            echo -e "${YELLOW}Running Test 4.3 - Zero-Sized Metadata Block${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "FullyQualifiedName~EdgeCasesAndBoundaryConditionsFeature.Test4_3" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        4.4|4_4)
            # Run specific test 4.4
            echo -e "${YELLOW}Running Test 4.4 - Minimum Buffer Sizes${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "FullyQualifiedName~Test4_4_MinimumBufferSizes" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        5.5|5_5)
            # Run specific test 5.5
            echo -e "${YELLOW}Running Test 5.5 - Wrap-Around With Wasted Space${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~Test5_5_Wrap_AroundWithWastedSpace" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        5.6|5_6)
            # Run specific test 5.6
            echo -e "${YELLOW}Running Test 5.6 - Continuous Free Space Calculation${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~Test5_6_ContinuousFreeSpaceCalculation" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        5.7|5_7)
            # Run specific test 5.7
            echo -e "${YELLOW}Running Test 5.7 - Maximum Frame Size${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~Test5_7_MaximumFrameSize" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        4.5|4_5)
            # Run specific test 4.5
            echo -e "${YELLOW}Running Test 4.5 - Reader Slower Than Writer${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~Test4_5_ReaderSlowerThanWriter" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        12.1|12_1)
            # Run specific test 12.1
            echo -e "${YELLOW}Running Test 12.1 - Protocol Compliance OIEB${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~Test12_1_ProtocolComplianceOIEB" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        12.2|12_2)
            # Run specific test 12.2
            echo -e "${YELLOW}Running Test 12.2 - Memory Alignment Verification${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "Name~Test12_2_MemoryAlignmentVerification" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        3.1|3_1)
            # Run specific test 3.1
            echo -e "${YELLOW}Running Test 3.1 - Metadata Write-Once Enforcement${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "FullyQualifiedName~Test3_1" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        3.2|3_2)
            # Run specific test 3.2
            echo -e "${YELLOW}Running Test 3.2 - Metadata Size Overflow${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "FullyQualifiedName~Test3_2" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        3.3|3_3)
            # Run specific test 3.3
            echo -e "${YELLOW}Running Test 3.3 - Zero Metadata Configuration${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "FullyQualifiedName~Test3_3" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        13.1|13_1)
            # Run specific test 13.1
            echo -e "${YELLOW}Running Test 13.1 - Basic Request-Response${NC}"
            dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj --filter "FullyQualifiedName~Test13_1" -v $VERBOSITY $NO_BUILD
            exit $?
            ;;
        *)
            # If not a flag, treat as test name filter
            SPECIFIC_TEST="$1"
            shift
            ;;
    esac
done

# Build filter expression
if [ -n "$SPECIFIC_TEST" ]; then
    FILTER_EXPR="FullyQualifiedName~$SPECIFIC_TEST"
elif [ -n "$FILTER" ]; then
    FILTER_EXPR="FullyQualifiedName~$FILTER"
else
    FILTER_EXPR=""
fi

# Construct the command
CMD="dotnet test ZeroBuffer.Tests/ZeroBuffer.Tests.csproj"
if [ -n "$FILTER_EXPR" ]; then
    CMD="$CMD --filter \"$FILTER_EXPR\""
fi
CMD="$CMD -v $VERBOSITY $NO_BUILD"

# Run the tests
echo -e "${YELLOW}Running tests...${NC}"
if [ -n "$FILTER_EXPR" ]; then
    echo "Filter: $FILTER_EXPR"
fi
echo "Command: $CMD"
echo ""

# Execute the command
if eval $CMD; then
    echo -e "\n${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "\n${RED}Some tests failed!${NC}"
    exit 1
fi