#!/bin/bash

# C++ → C# → C++ relay test
# Writer: C++, Relay: C#, Reader: C++

# Test parameters
BUFFER_INPUT="cpp_to_relay_test"
BUFFER_OUTPUT="relay_to_cpp_test"
FRAME_COUNT=100
FRAME_SIZE=1024
PATTERN="sequential"

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Running C++ → C# → C++ relay test${NC}"
echo "Input buffer: $BUFFER_INPUT"
echo "Output buffer: $BUFFER_OUTPUT"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes"
echo "Pattern: $PATTERN"
echo "-----------------------------------"

# Clean up any existing buffers
rm -f /dev/shm/$BUFFER_INPUT* 2>/dev/null
rm -f /dev/shm/$BUFFER_OUTPUT* 2>/dev/null

# Create results directory if it doesn't exist
mkdir -p ../../results

# Step 1: Start C# relay first (it creates the input buffer as Reader)
echo -e "${YELLOW}Starting C# relay...${NC}"
cd ../../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- relay "$BUFFER_INPUT" "$BUFFER_OUTPUT" \
    --frames $FRAME_COUNT \
    --verbose \
    --log-interval 10 \
    --json-output \
    > ../cross-platform-tests/results/cpp_relay_csharp.json 2>&1 &
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
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/cpp_relay_reader.json 2>&1 &
READER_PID=$!

# Give reader time to create the output buffer
sleep 2

# Step 3: Start C++ writer (connects to the input buffer)
echo -e "${YELLOW}Starting C++ writer...${NC}"
# Run writer in foreground and then sleep to keep connection alive
(./tests/zerobuffer-test-writer "$BUFFER_INPUT" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern $PATTERN \
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/cpp_relay_writer.json 2>&1 && sleep 5) &
WRITER_PID=$!

# Wait for all processes with timeout
echo -e "${YELLOW}Waiting for all processes to complete...${NC}"
TIMEOUT=30
ELAPSED=0

while [ $ELAPSED -lt $TIMEOUT ]; do
    # Check if all processes are done
    if ! kill -0 $WRITER_PID 2>/dev/null && \
       ! kill -0 $RELAY_PID 2>/dev/null && \
       ! kill -0 $READER_PID 2>/dev/null; then
        echo "All processes completed"
        break
    fi
    
    sleep 1
    ELAPSED=$((ELAPSED + 1))
    
    # Show progress
    if [ $((ELAPSED % 5)) -eq 0 ]; then
        echo "Still waiting... ($ELAPSED seconds)"
    fi
done

# If timeout, kill remaining processes
if [ $ELAPSED -ge $TIMEOUT ]; then
    echo -e "${RED}Timeout reached, killing processes${NC}"
    kill $WRITER_PID $RELAY_PID $READER_PID 2>/dev/null
fi

# Wait for processes to exit
wait $WRITER_PID
WRITER_EXIT=$?
wait $RELAY_PID
RELAY_EXIT=$?
wait $READER_PID
READER_EXIT=$?

# Check results
echo ""
echo "Results:"
echo "--------"
echo "Writer exit code: $WRITER_EXIT"
echo "Relay exit code: $RELAY_EXIT"
echo "Reader exit code: $READER_EXIT"

# Show detailed output
echo ""
echo -e "${YELLOW}Writer output:${NC}"
if [ -f ../../cross-platform-tests/results/cpp_relay_writer.json ]; then
    cat ../../cross-platform-tests/results/cpp_relay_writer.json | head -20
else
    echo "No writer output found"
fi

echo ""
echo -e "${YELLOW}Relay output:${NC}"
if [ -f ../../cross-platform-tests/results/cpp_relay_csharp.json ]; then
    cat ../../cross-platform-tests/results/cpp_relay_csharp.json | head -20
else
    echo "No relay output found"
fi

echo ""
echo -e "${YELLOW}Reader output:${NC}"
if [ -f ../../cross-platform-tests/results/cpp_relay_reader.json ]; then
    cat ../../cross-platform-tests/results/cpp_relay_reader.json | head -20
else
    echo "No reader output found"
fi

# Determine overall success
if [ $WRITER_EXIT -eq 0 ] && [ $RELAY_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo ""
    echo -e "${GREEN}Test PASSED${NC}"
    
    # Extract key metrics
    if [ -f ../../cross-platform-tests/results/cpp_relay_writer.json ] && \
       [ -f ../../cross-platform-tests/results/cpp_relay_csharp.json ] && \
       [ -f ../../cross-platform-tests/results/cpp_relay_reader.json ]; then
        
        WRITER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_writer.json'))['frames_written'])" 2>/dev/null || echo "?")
        RELAY_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_csharp.json'))['frames_relayed'])" 2>/dev/null || echo "?")
        READER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_reader.json'))['frames_read'])" 2>/dev/null || echo "?")
        READER_ERRORS=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_reader.json'))['verification_errors'])" 2>/dev/null || echo "?")
        
        echo ""
        echo "Summary:"
        echo "  Frames written: $WRITER_FRAMES"
        echo "  Frames relayed: $RELAY_FRAMES"
        echo "  Frames read: $READER_FRAMES"
        echo "  Verification errors: $READER_ERRORS"
    fi
    
    exit 0
else
    echo ""
    echo -e "${RED}Test FAILED${NC}"
    exit 1
fi