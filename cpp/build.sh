#!/bin/bash
# Standard build script for zerobuffer
# Usage: ./build.sh [Release|Debug] [clean]
#
# This script builds the essential targets needed for Harmony integration:
#   - Core ZeroBuffer library
#   - zerobuffer-serve (JSON-RPC server for Harmony)
#   - Cross-platform test executables (zerobuffer-test-reader/writer)
#   - Generated tests (if tests/generated exists)
#
# To build ALL targets including benchmarks and examples, use:
#   cmake .. -DBUILD_BENCHMARKS=ON -DBUILD_EXAMPLES=ON
#
# NOTE: Even minimal build may take 30-60 seconds depending on system performance

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default build type
BUILD_TYPE=${1:-Release}
BUILD_DIR="build"

# Track build timing
START_TIME=$(date +%s)

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Building ZeroBuffer - ${BUILD_TYPE}${NC}"
echo -e "${GREEN}=========================================${NC}"

# Show last build time if available (check in build directory)
if [ -f "${BUILD_DIR}/.last_build_time" ]; then
    LAST_BUILD_TIME=$(cat "${BUILD_DIR}/.last_build_time")
    echo -e "${BLUE}Last successful build took: ${LAST_BUILD_TIME} seconds${NC}"
fi

# Clean previous build if requested
if [ "$2" == "clean" ]; then
    echo -e "${YELLOW}Cleaning previous build...${NC}"
    rm -rf ${BUILD_DIR}
fi

# Create build directory
mkdir -p ${BUILD_DIR}
cd ${BUILD_DIR}

# Configure
echo -e "${YELLOW}Configuring CMake...${NC}"
# Build essential targets for Harmony integration
# Tests are now enabled by default to include generated tests
cmake .. -DCMAKE_BUILD_TYPE=${BUILD_TYPE} \
         -DBUILD_TESTS=ON \
         -DBUILD_BENCHMARKS=OFF \
         -DBUILD_EXAMPLES=OFF \
         -DBUILD_SERVE=ON

# Build
echo -e "${YELLOW}Building with $(nproc) cores...${NC}"
echo -e "${YELLOW}Building minimal targets. This should take 30-60 seconds...${NC}"
make -j$(nproc)

# Calculate and save build time
END_TIME=$(date +%s)
BUILD_DURATION=$((END_TIME - START_TIME))

# Save build time (create file in current directory which is build/)
echo "${BUILD_DURATION}" > ".last_build_time"

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Build complete!${NC}"
echo -e "${BLUE}Build time: ${BUILD_DURATION} seconds${NC}"
echo -e "${GREEN}Binaries location: $(pwd)${NC}"
echo -e "${GREEN}=========================================${NC}"