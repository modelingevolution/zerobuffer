#!/bin/bash
# Clean script for zerobuffer

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Cleaning ZeroBuffer${NC}"
echo -e "${GREEN}=========================================${NC}"

# Clean build directories
echo -e "${YELLOW}Removing build directories...${NC}"
rm -rf build build-debug build-release

# Clean shared memory resources
echo -e "${YELLOW}Cleaning shared memory resources...${NC}"
rm -f /dev/shm/zerobuffer-* 2>/dev/null || true
rm -f /dev/shm/test-* 2>/dev/null || true
rm -f /dev/shm/bench-* 2>/dev/null || true
rm -f /dev/shm/sem.zerobuffer-* 2>/dev/null || true
rm -f /dev/shm/sem.*test-* 2>/dev/null || true
rm -f /dev/shm/sem.*bench-* 2>/dev/null || true

# Clean temporary files
echo -e "${YELLOW}Cleaning temporary files...${NC}"
rm -f /tmp/zerobuffer-*.lock 2>/dev/null || true
rm -rf /tmp/zerobuffer 2>/dev/null || true

# Clean any core dumps
echo -e "${YELLOW}Cleaning core dumps...${NC}"
rm -f core.* 2>/dev/null || true

echo -e "${GREEN}Clean complete!${NC}"