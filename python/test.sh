#!/bin/bash

# Test runner script for ZeroBuffer Python BDD tests
# Runs tests from feature files using pytest-bdd

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Default values
FILTER=""
VERBOSITY=""
NO_BUILD=""
SPECIFIC_TEST=""
VENV_DIR="${VENV_DIR:-venv}"
NO_TYPE_CHECK=""

# Function to display usage
usage() {
    echo "Usage: $0 [options] [test-name]"
    echo "Options:"
    echo "  -f, --filter <pattern>    Filter tests by pattern (e.g., BasicCommunication)"
    echo "  -v, --verbose             Use detailed verbosity"
    echo "  -n, --no-build           Skip virtual environment check"
    echo "  -h, --help               Display this help message"
    echo ""
    echo "Examples:"
    echo "  $0                       # Run all BDD tests"
    echo "  $0 1.1                   # Run test 1.1 (Simple Write-Read Cycle)"
    echo "  $0 BasicCommunication    # Run all BasicCommunication tests"
    echo "  $0 -v edge              # Run edge case tests with verbose output"
    echo "  $0 4.3                  # Run specific test 4.3"
    echo "  $0 5.5 -v               # Run test 5.5 with verbose output"
    echo ""
    echo "Test Naming Convention:"
    echo "  1.1 - Simple Write-Read Cycle"
    echo "  1.2 - Multiple Frames Sequential"
    echo "  1.3 - Buffer Full Handling"
    echo "  1.4 - Zero-Copy Write Operations"
    echo "  1.5 - Mixed Frame Sizes"
    echo "  1.6 - Metadata Update During Operation"
    echo "  4.3 - Zero-Sized Metadata Block"
    echo "  4.4 - Minimum Buffer Sizes"
    echo "  5.5 - Wrap-Around With Wasted Space"
    echo "  ... (check feature files for full list)"
}

# Check virtual environment
check_venv() {
    if [ -z "$NO_BUILD" ]; then
        if [ ! -d "$VENV_DIR" ]; then
            echo -e "${RED}Virtual environment not found. Run ./build.sh first.${NC}"
            exit 1
        fi
        
        # Check if pytest-bdd is installed
        if ! "$VENV_DIR/bin/python" -c "import pytest_bdd" 2>/dev/null; then
            echo -e "${YELLOW}Installing pytest-bdd...${NC}"
            "$VENV_DIR/bin/pip" install -q pytest-bdd
        fi
        
        # Check if mypy is installed for type checking
        if ! "$VENV_DIR/bin/python" -c "import mypy" 2>/dev/null; then
            echo -e "${YELLOW}Installing mypy for type checking...${NC}"
            "$VENV_DIR/bin/pip" install -q mypy
        fi
        
        # CRITICAL: ALWAYS copy and fix feature files from source of truth
        # This MUST be done EVERY time because:
        # 1. Feature files in ZeroBuffer.Harmony.Tests are the source of truth
        # 2. Python's pytest-bdd doesn't support "And" steps like C#'s SpecFlow
        # 3. We need to convert "And" to proper Given/When/Then based on context
        # DO NOT REMOVE THIS! Tests will fail without proper feature file conversion!
        if [ -x "./copy_features.sh" ]; then
            echo -e "${YELLOW}Copying and fixing feature files from source of truth...${NC}"
            ./copy_features.sh > /dev/null 2>&1
            if [ $? -ne 0 ]; then
                echo -e "${RED}Failed to copy and fix feature files!${NC}"
                echo -e "${RED}Running copy_features.sh with output to debug:${NC}"
                ./copy_features.sh
                exit 1
            fi
        else
            echo -e "${RED}ERROR: copy_features.sh not found or not executable!${NC}"
            echo -e "${RED}This script is REQUIRED to convert 'And' steps for pytest-bdd${NC}"
            exit 1
        fi
        
        # Verify feature files are present after copying
        if [ -d "features" ] && [ "$(ls -A features/*.feature 2>/dev/null)" ]; then
            echo -e "${GREEN}✓ Feature files found${NC}"
        else
            echo -e "${RED}Error: Feature files not found in features/ directory${NC}"
            exit 1
        fi
    fi
}

# Run type checking with mypy
run_type_check() {
    echo -e "${YELLOW}Running type checking with mypy...${NC}"
    
    # Create mypy configuration if it doesn't exist
    if [ ! -f "mypy.ini" ]; then
        cat > mypy.ini << 'EOF'
[mypy]
python_version = 3.8
warn_return_any = True
warn_unused_configs = True
disallow_untyped_defs = True
disallow_any_unimported = False
no_implicit_optional = True
check_untyped_defs = True
warn_redundant_casts = True
warn_unused_ignores = True
warn_no_return = True
warn_unreachable = True
strict_equality = True

# Ignore missing imports for third-party libraries
[mypy-pytest.*]
ignore_missing_imports = True

[mypy-pytest_bdd.*]
ignore_missing_imports = True

[mypy-psutil.*]
ignore_missing_imports = True

[mypy-posix_ipc.*]
ignore_missing_imports = True
EOF
        echo -e "${GREEN}Created mypy.ini configuration${NC}"
    fi
    
    # Run mypy on the zerobuffer_serve directory
    if "$VENV_DIR/bin/mypy" zerobuffer_serve/ --config-file mypy.ini; then
        echo -e "${GREEN}✓ Type checking passed${NC}"
        return 0
    else
        echo -e "${RED}✗ Type checking failed${NC}"
        echo -e "${YELLOW}Fix type errors or use --no-type-check to skip${NC}"
        return 1
    fi
}

# Map test numbers to scenario names
get_scenario_name() {
    case "$1" in
        1.1|1_1)
            echo "Test 1.1 - Simple Write-Read Cycle"
            ;;
        1.2|1_2)
            echo "Test 1.2 - Multiple Frames Sequential"
            ;;
        1.3|1_3)
            echo "Test 1.3 - Buffer Full Handling"
            ;;
        1.4|1_4)
            echo "Test 1.4 - Zero-Copy Write Operations"
            ;;
        1.5|1_5)
            echo "Test 1.5 - Mixed Frame Sizes"
            ;;
        1.6|1_6)
            echo "Test 1.6 - Metadata Update During Operation"
            ;;
        2.1|2_1)
            echo "Test 2.1 - Clean Process Termination"
            ;;
        2.2|2_2)
            echo "Test 2.2 - Unexpected Process Termination"
            ;;
        3.1|3_1)
            echo "Test 3.1 - Writer Process Dies"
            ;;
        3.2|3_2)
            echo "Test 3.2 - Reader Process Dies"
            ;;
        4.1|4_1)
            echo "Test 4.1 - Single Frame Exact Buffer Size"
            ;;
        4.2|4_2)
            echo "Test 4.2 - Frame Larger Than Buffer"
            ;;
        4.3|4_3)
            echo "Test 4.3 - Zero-Sized Metadata Block"
            ;;
        4.4|4_4)
            echo "Test 4.4 - Minimum Buffer Sizes"
            ;;
        4.5|4_5)
            echo "Test 4.5 - Reader Slower Than Writer"
            ;;
        5.1|5_1)
            echo "Test 5.1 - Header Corruption Detection"
            ;;
        5.2|5_2)
            echo "Test 5.2 - Frame Data Corruption"
            ;;
        5.3|5_3)
            echo "Test 5.3 - Sequence Number Validation"
            ;;
        5.4|5_4)
            echo "Test 5.4 - Buffer State Recovery"
            ;;
        5.5|5_5)
            echo "Test 5.5 - Wrap-Around With Wasted Space"
            ;;
        5.6|5_6)
            echo "Test 5.6 - Continuous Free Space Calculation"
            ;;
        5.7|5_7)
            echo "Test 5.7 - Maximum Frame Size"
            ;;
        *)
            echo "$1"
            ;;
    esac
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        python|python_*|*_python)
            # Check if user is trying to run Harmony tests with local script
            echo -e "${RED}ERROR: You're trying to run Harmony cross-platform tests with the local test script!${NC}"
            echo -e "${RED}This script is for LOCAL Python tests only.${NC}"
            echo ""
            echo -e "${YELLOW}To run Harmony cross-platform tests, use:${NC}"
            echo -e "${GREEN}  ../test.sh $1 [test-number]${NC}"
            echo ""
            echo -e "${YELLOW}Examples:${NC}"
            echo -e "${GREEN}  ../test.sh python 1.1        # Run Python-Python test 1.1${NC}"
            echo -e "${GREEN}  ../test.sh python_csharp 1.2 # Run Python-C# test 1.2${NC}"
            echo -e "${GREEN}  ../test.sh csharp_python 1.3 # Run C#-Python test 1.3${NC}"
            echo ""
            echo -e "${YELLOW}For local Python tests, use:${NC}"
            echo -e "${GREEN}  ./test.sh 1.1                # Run test 1.1 locally${NC}"
            exit 1
            ;;
        -f|--filter)
            FILTER="$2"
            shift 2
            ;;
        -v|--verbose)
            VERBOSITY="-v -s"
            shift
            ;;
        -n|--no-build)
            NO_BUILD="true"
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
                "4.3"  # Edge case - Zero-Sized Metadata Block
                "4.4"  # Edge case - Minimum Buffer Sizes
                "4.5"  # Edge case - Reader Slower Than Writer
                "5.5"  # Corruption Detection - Wrap-Around With Wasted Space
                "5.6"  # Corruption Detection - Continuous Free Space Calculation
                "5.7"  # Corruption Detection - Maximum Frame Size
            )
            
            check_venv
            
            for test in "${TESTS[@]}"; do
                scenario=$(get_scenario_name "$test")
                echo -e "\n${YELLOW}Running: $scenario${NC}"
                if "$VENV_DIR/bin/pytest" tests/test_features_bdd.py -k "$scenario" $VERBOSITY; then
                    echo -e "${GREEN}✓ $test passed${NC}"
                else
                    echo -e "${RED}✗ $test failed${NC}"
                fi
            done
            exit 0
            ;;
        all)
            # Run all BDD tests
            check_venv
            echo -e "${YELLOW}Running all BDD tests...${NC}"
            "$VENV_DIR/bin/pytest" tests/test_features_bdd.py $VERBOSITY
            exit $?
            ;;
        [0-9].[0-9]|[0-9]_[0-9]|[0-9][0-9].[0-9]|[0-9][0-9]_[0-9])
            # Handle test number format (e.g., 1.1, 1_1, 12.1, 12_1)
            SPECIFIC_TEST="$1"
            shift
            ;;
        *)
            # If not a flag, treat as test name filter
            SPECIFIC_TEST="$1"
            shift
            ;;
    esac
done

# Check virtual environment
check_venv

# Run type checking unless disabled
if [ -z "$NO_TYPE_CHECK" ]; then
    if ! run_type_check; then
        echo -e "${RED}Type checking failed. Fix errors or use --no-type-check to skip.${NC}"
        exit 1
    fi
    echo ""
fi

# Build test command
if [ -n "$SPECIFIC_TEST" ]; then
    # Check if it's a test number
    if [[ "$SPECIFIC_TEST" =~ ^[0-9]+[._][0-9]+$ ]]; then
        scenario=$(get_scenario_name "$SPECIFIC_TEST")
        echo -e "${YELLOW}Running: $scenario${NC}"
        # Convert test number to method name format (1.2 -> test_1_2)
        test_method_prefix="test_${SPECIFIC_TEST//./_}"
        
        # Search for test in all test files
        found_test=""
        for test_file in tests/test_*.py; do
            if [ -f "$test_file" ] && grep -q "def ${test_method_prefix}_" "$test_file" 2>/dev/null; then
                found_test="$test_file"
                # Get the full test method name
                test_method=$(grep -o "def ${test_method_prefix}_[a-z_]*" "$test_file" | head -1 | sed 's/def //')
                break
            fi
        done
        
        if [ -n "$found_test" ]; then
            # Found the test in a specific file
            # Check if it's in a test class (look for class definition anywhere in file)
            if grep -q "^class " "$found_test"; then
                # Get the class name (assumes tests are in the first class found)
                class_name=$(grep "^class " "$found_test" | head -1 | sed 's/class \([^(:]*\).*/\1/')
                CMD="$VENV_DIR/bin/pytest ${found_test}::${class_name}::${test_method} $VERBOSITY"
            else
                # Standalone test function
                CMD="$VENV_DIR/bin/pytest ${found_test}::${test_method} $VERBOSITY"
            fi
        else
            # Fall back to searching by scenario name
            scenario_escaped=$(echo "$scenario" | sed 's/ /_/g' | sed 's/-/_/g')
            CMD="$VENV_DIR/bin/pytest tests/ -k \"${scenario_escaped}\" $VERBOSITY"
        fi
    else
        # Treat as a filter pattern
        echo -e "${YELLOW}Running tests matching: $SPECIFIC_TEST${NC}"
        CMD="$VENV_DIR/bin/pytest tests/test_features_bdd.py -k \"$SPECIFIC_TEST\" $VERBOSITY"
    fi
elif [ -n "$FILTER" ]; then
    echo -e "${YELLOW}Running tests matching filter: $FILTER${NC}"
    CMD="$VENV_DIR/bin/pytest tests/test_features_bdd.py -k \"$FILTER\" $VERBOSITY"
else
    echo -e "${YELLOW}Running all BDD tests...${NC}"
    CMD="$VENV_DIR/bin/pytest tests/test_features_bdd.py $VERBOSITY"
fi

# Show feature file location
FEATURE_DIR="../ZeroBuffer.Harmony.Tests/Features"
if [ -d "$FEATURE_DIR" ]; then
    echo -e "${BLUE}Reading features from: $FEATURE_DIR${NC}"
else
    FEATURE_DIR="../csharp/ZeroBuffer.Tests/Features"
    if [ -d "$FEATURE_DIR" ]; then
        echo -e "${BLUE}Reading features from: $FEATURE_DIR${NC}"
    else
        echo -e "${RED}Warning: Feature files not found${NC}"
    fi
fi

# Execute the test command
echo -e "${BLUE}Executing: $CMD${NC}"
echo ""

eval $CMD
exit_code=$?

if [ $exit_code -eq 0 ]; then
    echo -e "\n${GREEN}Tests passed! ✓${NC}"
else
    echo -e "\n${RED}Tests failed! ✗${NC}"
fi

exit $exit_code