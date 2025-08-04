#!/bin/bash

# Cross-Platform ZeroBuffer Test Suite
# Runs all interoperability tests between C++, C#, and Python

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS_DIR="$SCRIPT_DIR/results"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS_FILE="$RESULTS_DIR/test_results_$TIMESTAMP.json"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "ZeroBuffer Cross-Platform Test Suite"
echo "========================================="
echo ""

# Check if all implementations are built
check_prerequisites() {
    echo "Checking prerequisites..."
    
    # Check C++ build
    if [ ! -f "../cpp/build/examples/example_writer" ]; then
        echo -e "${RED}Error: C++ not built. Run './build.sh' in cpp directory${NC}"
        exit 1
    fi
    
    # Check C# build
    if [ ! -f "../csharp/ZeroBuffer.TestHelper/bin/Debug/net9.0/ZeroBuffer.TestHelper.dll" ]; then
        echo -e "${RED}Error: C# not built. Run 'dotnet build' in csharp directory${NC}"
        exit 1
    fi
    
    # Check Python
    if ! python3 -c "import zerobuffer" 2>/dev/null; then
        echo -e "${RED}Error: Python zerobuffer not installed. Run 'pip install -e .' in python directory${NC}"
        exit 1
    fi
    
    echo -e "${GREEN}All prerequisites satisfied${NC}"
    echo ""
}

# Initialize results file
init_results() {
    mkdir -p "$RESULTS_DIR"
    echo "{" > "$RESULTS_FILE"
    echo "  \"timestamp\": \"$TIMESTAMP\"," >> "$RESULTS_FILE"
    echo "  \"platform\": \"$(uname -s)\"," >> "$RESULTS_FILE"
    echo "  \"tests\": [" >> "$RESULTS_FILE"
}

# Run round-trip tests
run_round_trip_tests() {
    echo -e "${YELLOW}Running Round-Trip Tests${NC}"
    echo "========================="
    
    # C++ to C#
    echo -n "C++ → C#: "
    if $SCRIPT_DIR/round-trip/cpp-csharp/run_test.sh >> "$RESULTS_DIR/cpp_csharp_$TIMESTAMP.log" 2>&1; then
        echo -e "${GREEN}PASSED${NC}"
    else
        echo -e "${RED}FAILED${NC}"
    fi
    
    # C# to C++
    echo -n "C# → C++: "
    if $SCRIPT_DIR/round-trip/cpp-csharp/run_test_reverse.sh >> "$RESULTS_DIR/csharp_cpp_$TIMESTAMP.log" 2>&1; then
        echo -e "${GREEN}PASSED${NC}"
    else
        echo -e "${RED}FAILED${NC}"
    fi
    
    # C++ to Python
    echo -n "C++ → Python: "
    if $SCRIPT_DIR/round-trip/cpp-python/run_test.sh >> "$RESULTS_DIR/cpp_python_$TIMESTAMP.log" 2>&1; then
        echo -e "${GREEN}PASSED${NC}"
    else
        echo -e "${RED}FAILED${NC}"
    fi
    
    # Python to C++
    echo -n "Python → C++: "
    if $SCRIPT_DIR/round-trip/cpp-python/run_test_reverse.sh >> "$RESULTS_DIR/python_cpp_$TIMESTAMP.log" 2>&1; then
        echo -e "${GREEN}PASSED${NC}"
    else
        echo -e "${RED}FAILED${NC}"
    fi
    
    # C# to Python
    echo -n "C# → Python: "
    if $SCRIPT_DIR/round-trip/csharp-python/run_test.sh >> "$RESULTS_DIR/csharp_python_$TIMESTAMP.log" 2>&1; then
        echo -e "${GREEN}PASSED${NC}"
    else
        echo -e "${RED}FAILED${NC}"
    fi
    
    # Python to C#
    echo -n "Python → C#: "
    if $SCRIPT_DIR/round-trip/csharp-python/run_test_reverse.sh >> "$RESULTS_DIR/python_csharp_$TIMESTAMP.log" 2>&1; then
        echo -e "${GREEN}PASSED${NC}"
    else
        echo -e "${RED}FAILED${NC}"
    fi
    
    echo ""
}

# Run relay tests
run_relay_tests() {
    echo -e "${YELLOW}Running Relay Tests${NC}"
    echo "==================="
    
    echo "Relay tests: C++ → C# → Python"
    echo "Relay tests: C++ → Python → C#"
    echo "Relay tests: C# → C++ → Python"
    echo "Relay tests: C# → Python → C++"
    echo "Relay tests: Python → C++ → C#"
    echo "Relay tests: Python → C# → C++"
    
    echo -e "${YELLOW}[Relay tests to be implemented]${NC}"
    echo ""
}

# Run compatibility tests
run_compatibility_tests() {
    echo -e "${YELLOW}Running Compatibility Tests${NC}"
    echo "==========================="
    
    echo "Buffer Creation: Testing cross-platform buffer discovery..."
    echo "Metadata: Testing metadata interchange..."
    echo "Wrap-Around: Testing buffer wrap behavior..."
    echo "Resource Cleanup: Testing cleanup across platforms..."
    echo "Semaphores: Testing semaphore compatibility..."
    
    echo -e "${YELLOW}[Compatibility tests to be implemented]${NC}"
    echo ""
}

# Finalize results
finalize_results() {
    echo "  ]" >> "$RESULTS_FILE"
    echo "}" >> "$RESULTS_FILE"
    
    echo "========================================="
    echo -e "${GREEN}Test run complete!${NC}"
    echo "Results saved to: $RESULTS_FILE"
    echo "Logs saved to: $RESULTS_DIR/*_$TIMESTAMP.log"
    echo "========================================="
}

# Main execution
check_prerequisites
init_results
run_round_trip_tests
run_relay_tests
run_compatibility_tests
finalize_results