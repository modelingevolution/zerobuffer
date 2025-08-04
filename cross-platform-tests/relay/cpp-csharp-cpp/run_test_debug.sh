#!/bin/bash

# C++ → C# → C++ relay test with debug output
# Writer: C++, Relay: C#, Reader: C++

# Test parameters
BUFFER_INPUT="cpp_to_relay_test"
BUFFER_OUTPUT="relay_to_cpp_test"
FRAME_COUNT=10
FRAME_SIZE=1024
PATTERN="sequential"

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Running C++ → C# → C++ relay test (DEBUG MODE)${NC}"
echo "Input buffer: $BUFFER_INPUT"
echo "Output buffer: $BUFFER_OUTPUT"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes"
echo "Pattern: $PATTERN"
echo "-----------------------------------"

# Clean up any existing buffers
rm -f /dev/shm/$BUFFER_INPUT* 2>/dev/null
rm -f /dev/shm/$BUFFER_OUTPUT* 2>/dev/null
rm -f /dev/shm/sem.sem-* 2>/dev/null

# Create results directory if it doesn't exist
mkdir -p ../../results

# Step 1: Start C# relay first (it creates the input buffer as Reader)
echo -e "${YELLOW}Starting C# relay...${NC}"
cd ../../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- relay "$BUFFER_INPUT" "$BUFFER_OUTPUT" \
    --frames $FRAME_COUNT \
    --verbose \
    --log-interval 1 &
RELAY_PID=$!

# Give relay time to create the input buffer
sleep 3

# Step 2: Start C++ reader (creates the output buffer)
echo -e "${YELLOW}Starting C++ reader...${NC}"
cd ../cpp/build
./tests/zerobuffer-test-reader "$BUFFER_OUTPUT" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify $PATTERN \
    --verbose &
READER_PID=$!

# Give reader time to create the output buffer
sleep 2

# Step 3: Start C++ writer (connects to the input buffer)
echo -e "${YELLOW}Starting C++ writer...${NC}"
./tests/zerobuffer-test-writer "$BUFFER_INPUT" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern $PATTERN \
    --verbose &
WRITER_PID=$!

# Wait for writer to complete
wait $WRITER_PID
WRITER_EXIT=$?
echo -e "${YELLOW}Writer completed with exit code: $WRITER_EXIT${NC}"

# Give relay time to process all frames
sleep 3

# Kill remaining processes
echo -e "${YELLOW}Stopping relay and reader...${NC}"
kill $RELAY_PID $READER_PID 2>/dev/null

# Wait for them to exit
wait $RELAY_PID
RELAY_EXIT=$?
wait $READER_PID  
READER_EXIT=$?

echo ""
echo "Results:"
echo "--------"
echo "Writer exit code: $WRITER_EXIT"
echo "Relay exit code: $RELAY_EXIT"
echo "Reader exit code: $READER_EXIT"

# Determine overall success
if [ $WRITER_EXIT -eq 0 ]; then
    echo -e "${GREEN}Test completed${NC}"
else
    echo -e "${RED}Test FAILED${NC}"
fi