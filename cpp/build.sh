#!/bin/bash
# Standard build script for zerobuffer
# Usage: ./build.sh [Release|Debug] [clean]

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Default build type
BUILD_TYPE=${1:-Release}
BUILD_DIR="build"

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Building ZeroBuffer - ${BUILD_TYPE}${NC}"
echo -e "${GREEN}=========================================${NC}"

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
cmake .. -DCMAKE_BUILD_TYPE=${BUILD_TYPE} \
         -DBUILD_TESTS=ON \
         -DBUILD_BENCHMARKS=ON \
         -DBUILD_EXAMPLES=ON

# Build
echo -e "${YELLOW}Building with $(nproc) cores...${NC}"
make -j$(nproc)

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Build complete!${NC}"
echo -e "${GREEN}Binaries location: $(pwd)${NC}"
echo -e "${GREEN}=========================================${NC}"