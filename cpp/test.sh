#!/bin/bash
# Standard test script for zerobuffer
# Usage: ./test.sh [unit|benchmark|all]

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Test type
TEST_TYPE=${1:-all}

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
    
    # First ensure test_duplex_channel is built
    if [ ! -f "tests/test_duplex_channel" ]; then
        echo -e "${YELLOW}Building test_duplex_channel...${NC}"
        make test_duplex_channel
    fi
    
    # Run tests directly since ctest might not find them
    if [ -f "tests/test_duplex_channel" ]; then
        echo -e "${YELLOW}Running duplex channel tests...${NC}"
        ./tests/test_duplex_channel
        echo -e "${GREEN}✓ Duplex channel tests passed${NC}"
    fi
    
    if [ -f "tests/test_duplex_simple" ]; then
        echo -e "${YELLOW}Running simple duplex test...${NC}"
        ./tests/test_duplex_simple
        echo -e "${GREEN}✓ Simple duplex test passed${NC}"
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