#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "Running Code Quality Checks"
echo "========================================="

# Track overall status
EXIT_CODE=0

# Activate virtual environment if it exists
if [ -d "venv" ]; then
    echo -e "${YELLOW}Activating virtual environment...${NC}"
    source venv/bin/activate
elif [ -d ".venv" ]; then
    echo -e "${YELLOW}Activating virtual environment...${NC}"
    source .venv/bin/activate
elif [ -d "env" ]; then
    echo -e "${YELLOW}Activating virtual environment...${NC}"
    source env/bin/activate
else
    echo -e "${YELLOW}No virtual environment found. Using system Python.${NC}"
fi

# Run mypy type checking
echo -e "\n${YELLOW}Running mypy type checking...${NC}"
if python3 -m mypy zerobuffer tests --config-file mypy.ini; then
    echo -e "${GREEN}✓ Type checking passed${NC}"
else
    echo -e "${RED}✗ Type checking failed${NC}"
    EXIT_CODE=1
fi

# Summary
echo -e "\n========================================="
if [ $EXIT_CODE -eq 0 ]; then
    echo -e "${GREEN}All code quality checks passed!${NC}"
else
    echo -e "${RED}Some code quality checks failed!${NC}"
fi
echo "========================================="

exit $EXIT_CODE
