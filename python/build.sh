#!/usr/bin/env bash
# Build script for ZeroBuffer Python
# Following DevOps best practices with error handling and logging

set -euo pipefail  # Exit on error, undefined variables, pipe failures

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Default values
BUILD_TYPE="${1:-release}"
PYTHON="${PYTHON:-python3}"
VENV_DIR="${VENV_DIR:-venv}"

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

# Check Python version
check_python_version() {
    log_info "Checking Python version..."
    
    if ! command -v "$PYTHON" &> /dev/null; then
        log_error "Python not found. Please install Python 3.8 or later."
        exit 1
    fi
    
    # Get Python version
    python_version=$($PYTHON -c 'import sys; print(".".join(map(str, sys.version_info[:2])))')
    required_version="3.8"
    
    if [[ $(echo "$python_version < $required_version" | bc) -eq 1 ]]; then
        log_error "Python $required_version or later is required. Found: $python_version"
        exit 1
    fi
    
    log_info "Python $python_version found"
}

# Create virtual environment
create_venv() {
    if [[ ! -d "$VENV_DIR" ]]; then
        log_info "Creating virtual environment..."
        $PYTHON -m venv "$VENV_DIR"
    else
        log_info "Virtual environment already exists"
    fi
}

# Activate virtual environment
activate_venv() {
    log_info "Activating virtual environment..."
    source "$VENV_DIR/bin/activate"
}

# Install dependencies
install_dependencies() {
    log_info "Installing dependencies..."
    
    # Upgrade pip
    pip install --upgrade pip setuptools wheel --quiet
    
    # Install platform-specific dependencies
    if [[ "$OSTYPE" == "linux-gnu"* ]] || [[ "$OSTYPE" == "darwin"* ]]; then
        pip install posix-ipc --quiet
    elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]]; then
        pip install pywin32 --quiet
    fi
    
    # Install test dependencies if in debug mode
    if [[ "$BUILD_TYPE" == "debug" ]] || [[ "$BUILD_TYPE" == "test" ]]; then
        pip install -r requirements-dev.txt --quiet
    fi
}

# Install package
install_package() {
    log_info "Installing zerobuffer package..."
    
    if [[ "$BUILD_TYPE" == "release" ]]; then
        pip install . --quiet
    else
        pip install -e . --quiet
    fi
}

# Run tests
run_tests() {
    if [[ "$BUILD_TYPE" == "test" ]] || [[ "$BUILD_TYPE" == "debug" ]]; then
        log_info "Running tests..."
        python -m pytest tests/ -v --tb=short || {
            log_error "Tests failed!"
            exit 1
        }
    fi
}

# Build distribution
build_dist() {
    if [[ "$BUILD_TYPE" == "release" ]]; then
        log_info "Building distribution packages..."
        
        # Clean old builds
        rm -rf build/ dist/ *.egg-info
        
        # Build source and wheel distributions
        python -m build
        
        log_info "Distribution packages built in dist/"
        ls -la dist/
    fi
}

# Main build process
main() {
    log_info "Starting ZeroBuffer Python build (${BUILD_TYPE})"
    log_info "======================================"
    
    # Check prerequisites
    check_python_version
    
    # Set up environment
    create_venv
    activate_venv
    
    # Install
    install_dependencies
    install_package
    
    # Test
    run_tests
    
    # Build distribution if release
    build_dist
    
    log_info "======================================"
    log_info "Build completed successfully!"
    
    if [[ "$BUILD_TYPE" != "release" ]]; then
        log_info "To activate the virtual environment, run:"
        log_info "  source $VENV_DIR/bin/activate"
    fi
}

# Handle script arguments
case "${BUILD_TYPE,,}" in
    release|debug|test)
        main
        ;;
    clean)
        log_info "Cleaning build artifacts..."
        rm -rf build/ dist/ *.egg-info .pytest_cache/ htmlcov/ .coverage
        find . -type d -name '__pycache__' -exec rm -rf {} + 2>/dev/null || true
        find . -type f -name '*.pyc' -delete
        log_info "Clean complete"
        ;;
    *)
        echo "Usage: $0 [release|debug|test|clean]"
        echo "  release - Build optimized release version"
        echo "  debug   - Build with development dependencies"
        echo "  test    - Build and run tests"
        echo "  clean   - Remove build artifacts"
        exit 1
        ;;
esac