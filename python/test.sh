#!/usr/bin/env bash
# Test runner script for ZeroBuffer Python
# DevOps best practices: parallel execution, coverage, reporting

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Configuration
VENV_DIR="${VENV_DIR:-venv}"
TEST_TYPE="${1:-all}"
PYTEST_ARGS="${PYTEST_ARGS:-}"
COVERAGE_MIN="${COVERAGE_MIN:-80}"

# Logging functions
log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_test() {
    echo -e "${BLUE}[TEST]${NC} $1"
}

# Check environment
check_environment() {
    if [[ ! -d "$VENV_DIR" ]]; then
        log_error "Virtual environment not found. Run ./build.sh first."
        exit 1
    fi
    
    # Activate virtual environment
    source "$VENV_DIR/bin/activate"
    
    # Check if pytest is installed
    if ! command -v pytest &> /dev/null; then
        log_error "pytest not found. Installing test dependencies..."
        pip install -r requirements-dev.txt --quiet
    fi
}

# Clean test artifacts
clean_test_artifacts() {
    log_info "Cleaning test artifacts..."
    rm -rf .pytest_cache/
    rm -rf htmlcov/
    rm -f .coverage
    rm -f coverage.xml
    
    # Clean shared memory artifacts (Linux/macOS)
    if [[ "$OSTYPE" == "linux-gnu"* ]] || [[ "$OSTYPE" == "darwin"* ]]; then
        # Clean up any stale shared memory segments
        if command -v ipcrm &> /dev/null; then
            ipcs -m | grep "$USER" | grep "test_" | awk '{print $2}' | xargs -r ipcrm -m 2>/dev/null || true
        fi
        
        # Clean up lock files
        rm -rf /tmp/zerobuffer/test_* 2>/dev/null || true
    fi
}

# Run unit tests
run_unit_tests() {
    log_test "Running unit tests..."
    
    pytest tests/test_zerobuffer.py \
        -v \
        --tb=short \
        --timeout=30 \
        --color=yes \
        $PYTEST_ARGS || return 1
}

# Run integration tests
run_integration_tests() {
    log_test "Running integration tests..."
    
    pytest tests/test_scenarios.py \
        -v \
        --tb=short \
        --timeout=60 \
        --color=yes \
        $PYTEST_ARGS || return 1
}

# Run tests with coverage
run_coverage() {
    log_test "Running tests with coverage..."
    
    pytest tests/ \
        --cov=zerobuffer \
        --cov-report=term-missing \
        --cov-report=html \
        --cov-report=xml \
        --cov-fail-under=$COVERAGE_MIN \
        -v \
        --tb=short \
        --color=yes \
        $PYTEST_ARGS || return 1
    
    log_info "Coverage report generated in htmlcov/index.html"
}

# Run performance tests
run_performance_tests() {
    log_test "Running performance tests..."
    
    # Run throughput test
    pytest tests/test_scenarios.py::TestScenario11Performance::test_throughput \
        -v \
        -s \
        --tb=short \
        $PYTEST_ARGS || return 1
}

# Run linting
run_lint() {
    log_test "Running code quality checks..."
    
    # Check if linting tools are installed
    if ! command -v flake8 &> /dev/null; then
        pip install flake8 black isort mypy --quiet
    fi
    
    # Run flake8
    log_info "Running flake8..."
    flake8 zerobuffer tests --max-line-length=120 --ignore=E203,W503 || true
    
    # Run black in check mode
    log_info "Checking code formatting..."
    black zerobuffer tests --check --line-length=120 || {
        log_warn "Code formatting issues found. Run 'make format' to fix."
    }
    
    # Run isort in check mode
    log_info "Checking import ordering..."
    isort zerobuffer tests --check-only || {
        log_warn "Import ordering issues found. Run 'make format' to fix."
    }
}

# Generate test report
generate_report() {
    log_info "Generating test report..."
    
    # Create reports directory
    mkdir -p reports
    
    # Generate summary
    cat > reports/test_summary.txt << EOF
ZeroBuffer Python Test Report
=============================
Date: $(date)
Python Version: $(python --version 2>&1)
Platform: $(uname -s)

Test Results:
EOF
    
    # Add test results if available
    if [[ -f .coverage ]]; then
        echo -e "\nCoverage Summary:" >> reports/test_summary.txt
        coverage report >> reports/test_summary.txt
    fi
    
    log_info "Test report saved to reports/test_summary.txt"
}

# Main test execution
main() {
    log_info "ZeroBuffer Python Test Suite"
    log_info "============================"
    
    # Check environment
    check_environment
    
    # Clean artifacts
    clean_test_artifacts
    
    # Track failures
    failed=0
    
    case "$TEST_TYPE" in
        unit)
            run_unit_tests || failed=1
            ;;
        integration)
            run_integration_tests || failed=1
            ;;
        coverage)
            run_coverage || failed=1
            ;;
        performance)
            run_performance_tests || failed=1
            ;;
        lint)
            run_lint || failed=1
            ;;
        all)
            log_info "Running all tests..."
            run_unit_tests || failed=1
            run_integration_tests || failed=1
            run_coverage || failed=1
            run_lint || failed=1
            ;;
        quick)
            log_info "Running quick test suite..."
            PYTEST_ARGS="-x" run_unit_tests || failed=1
            ;;
        *)
            echo "Usage: $0 [all|unit|integration|coverage|performance|lint|quick]"
            echo "  all         - Run all tests with coverage"
            echo "  unit        - Run unit tests only"
            echo "  integration - Run integration tests only"
            echo "  coverage    - Run all tests with coverage report"
            echo "  performance - Run performance benchmarks"
            echo "  lint        - Run code quality checks"
            echo "  quick       - Run unit tests, stop on first failure"
            exit 1
            ;;
    esac
    
    # Generate report
    generate_report
    
    # Clean up after tests
    clean_test_artifacts
    
    # Report results
    if [[ $failed -eq 0 ]]; then
        log_info "============================"
        log_info "All tests passed! ✓"
        exit 0
    else
        log_error "============================"
        log_error "Some tests failed! ✗"
        exit 1
    fi
}

# Run main function
main