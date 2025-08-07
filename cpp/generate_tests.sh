#!/bin/bash
# Generate C++ Google Tests from feature files using harmony-cpp-gen

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}C++ Test Generator (harmony-cpp-gen)${NC}"
echo -e "${GREEN}=========================================${NC}"

# Check if harmony-cpp-gen is installed
if ! command -v harmony-cpp-gen &> /dev/null; then
    echo -e "${RED}Error: harmony-cpp-gen is not installed${NC}"
    echo -e "${YELLOW}Installing harmony-cpp-gen...${NC}"
    
    # Build and install the tool
    echo -e "${BLUE}Building tool from source...${NC}"
    cd ../tools/harmony-cpp-gen
    dotnet build
    dotnet tool install --global --add-source ./nupkg HarmonyCppGen
    cd ../../cpp
    
    echo -e "${GREEN}✓ harmony-cpp-gen installed successfully${NC}"
fi

# Run the generator
echo -e "${YELLOW}Generating C++ tests from feature files...${NC}"
harmony-cpp-gen \
    --input ../ZeroBuffer.Harmony.Tests/Features \
    --output tests/generated \
    --namespace zerobuffer::steps

echo -e "${GREEN}Generation complete!${NC}"
echo -e "${GREEN}Generated files are in: tests/generated/${NC}"

# Offer to rebuild with generated tests
echo ""
echo -e "${YELLOW}Do you want to rebuild with the generated tests? (y/n)${NC}"
read -r response
if [[ "$response" =~ ^[Yy]$ ]]; then
    ./build.sh
fi