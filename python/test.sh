#!/bin/bash

# Test runner script for ZeroBuffer Python
# Usage: ./test.sh [unit|1.1|1.2|scenarios|all]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Ensure we're in the right directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Activate virtual environment if it exists
if [ -d "venv" ]; then
    source venv/bin/activate
fi

# Function to run tests with nice output
run_test() {
    local test_name=$1
    local test_command=$2
    
    echo -e "${YELLOW}Running $test_name...${NC}"
    if $test_command; then
        echo -e "${GREEN}✓ $test_name passed${NC}\n"
        return 0
    else
        echo -e "${RED}✗ $test_name failed${NC}\n"
        return 1
    fi
}

# Parse command line arguments
TEST_TYPE=${1:-all}

case $TEST_TYPE in
    unit)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Unit Tests (matching C# unit tests)${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Core ZeroBuffer Tests" "pytest tests/test_zerobuffer.py -v --tb=short"
        run_test "Duplex Channel Tests" "pytest tests/test_duplex.py tests/test_duplex_channel.py tests/test_duplex_channel_integration.py -v --tb=short"
        ;;
        
    1.1)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Scenario 1.1 - Simple Write-Read Cycle${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario 1.1" "pytest tests/test_scenario_1_1.py -v --tb=short"
        ;;
        
    1.2)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Scenario 1.2 - Multiple Frames Sequential${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario 1.2" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_2_multiple_frames_sequential -v --tb=short"
        ;;
        
    1.3)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Scenario 1.3 - Buffer Full Handling${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario 1.3" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_3_buffer_full_handling -v --tb=short"
        ;;
        
    1.4)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Scenario 1.4 - Zero-Copy Write Operations${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario 1.4" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_4_zero_copy_write_operations -v --tb=short"
        ;;
        
    1.5)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Scenario 1.5 - Mixed Frame Sizes${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario 1.5" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_5_mixed_frame_sizes -v --tb=short"
        ;;
        
    1.*)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running All Scenario 1.x Tests${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario 1.1" "pytest tests/test_scenario_1_1.py -v --tb=short"
        run_test "Scenario 1.2" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_2_multiple_frames_sequential -v --tb=short"
        run_test "Scenario 1.3" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_3_buffer_full_handling -v --tb=short"
        run_test "Scenario 1.4" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_4_zero_copy_write_operations -v --tb=short"
        run_test "Scenario 1.5" "pytest tests/test_basic_communication.py::TestBasicCommunication::test_1_5_mixed_frame_sizes -v --tb=short"
        ;;
        
    scenarios)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running All Scenario Tests${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Scenario Tests" "pytest tests/test_scenarios.py -v --tb=short"
        ;;
        
    advanced)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Advanced Tests${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Advanced Tests" "pytest tests/test_advanced.py -v --tb=short"
        ;;
        
    duplex)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Duplex Channel Tests${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Duplex Tests" "pytest tests/test_duplex.py tests/test_duplex_channel.py tests/test_duplex_channel_integration.py -v --tb=short"
        ;;
        
    all)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running All Tests${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        # Run tests in order of importance
        run_test "Unit Tests" "pytest tests/test_zerobuffer.py -v --tb=short"
        run_test "Scenario 1.1" "pytest tests/test_scenario_1_1.py -v --tb=short"
        run_test "Basic Communication" "pytest tests/test_basic_communication.py -v --tb=short"
        run_test "Scenario Tests" "pytest tests/test_scenarios.py -v --tb=short"
        run_test "Advanced Tests" "pytest tests/test_advanced.py -v --tb=short"
        run_test "Duplex Tests" "pytest tests/test_duplex.py tests/test_duplex_channel.py -v --tb=short"
        ;;
        
    quick)
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}"
        echo -e "${YELLOW}Running Quick Tests (Unit + Scenario 1.1)${NC}"
        echo -e "${YELLOW}════════════════════════════════════════════════════════${NC}\n"
        
        run_test "Unit Tests" "pytest tests/test_zerobuffer.py -v --tb=short"
        run_test "Scenario 1.1" "pytest tests/test_scenario_1_1.py -v --tb=short"
        ;;
        
    *)
        echo "Usage: $0 [unit|1.1|1.2|1.3|1.4|1.5|scenarios|advanced|duplex|all|quick]"
        echo ""
        echo "Test categories:"
        echo "  unit       - Run core unit tests (matching C# unit tests)"
        echo "  1.1        - Run Scenario 1.1: Simple Write-Read Cycle"
        echo "  1.2        - Run Scenario 1.2: Multiple Frames Sequential"
        echo "  1.3        - Run Scenario 1.3: Buffer Full Handling"
        echo "  1.4        - Run Scenario 1.4: Zero-Copy Write Operations"
        echo "  1.5        - Run Scenario 1.5: Mixed Frame Sizes"
        echo "  scenarios  - Run all scenario tests"
        echo "  advanced   - Run advanced tests"
        echo "  duplex     - Run duplex channel tests"
        echo "  all        - Run all tests"
        echo "  quick      - Run unit tests + scenario 1.1 (quick smoke test)"
        exit 1
        ;;
esac

echo -e "${GREEN}Test run complete!${NC}"