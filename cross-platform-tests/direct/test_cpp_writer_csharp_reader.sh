#!/bin/bash

# Direct C++ writer to C# reader test focusing on wrap-around
# This test will help isolate wrap-around issues

BUFFER_NAME="cpp_csharp_direct_test"
FRAME_COUNT=100  # Enough to cause wrap-around (86 frames fit in 256MB)
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Running Direct C# Writer → C++ Reader test${NC}"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT (wrap-around after 86)"
echo "Frame size: $FRAME_SIZE bytes"
echo "-----------------------------------"

# Clean up any existing buffers
rm -f /dev/shm/$BUFFER_NAME* 2>/dev/null
rm -f /dev/shm/sem.sem-* 2>/dev/null

# Create results directory
mkdir -p ../results

# Step 1: Start C++ reader first (it creates the buffer with 256MB)
echo -e "${YELLOW}Starting C++ reader...${NC}"
cd ../../cpp/build
./tests/zerobuffer-test-reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify sequential \
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/direct_cpp_reader.json 2>&1 &
READER_PID=$!

# Give reader time to create the buffer
sleep 3

# Step 2: Start C# writer (connects to existing buffer)
echo -e "${YELLOW}Starting C# writer...${NC}"
cd ../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    --json-output \
    > ../cross-platform-tests/results/direct_csharp_writer.json 2>&1 &
WRITER_PID=$!

# Wait for writer to complete
wait $WRITER_PID
WRITER_EXIT=$?

# Give reader time to finish
echo -e "${YELLOW}Waiting for reader to finish...${NC}"
sleep 10

# Kill reader if still running
if kill -0 $READER_PID 2>/dev/null; then
    echo "Reader still running, killing..."
    kill $READER_PID
fi

wait $READER_PID 2>/dev/null
READER_EXIT=$?

# Check results
echo ""
echo "Results:"
echo "--------"
echo "Writer exit code: $WRITER_EXIT"
echo "Reader exit code: $READER_EXIT"

# Show summary from JSON outputs
echo ""
echo -e "${YELLOW}Summary:${NC}"

if [ -f ../../cross-platform-tests/results/direct_csharp_writer.json ]; then
    WRITER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/direct_csharp_writer.json')).get('FramesWritten', '?'))" 2>/dev/null || echo "?")
    echo "Writer: $WRITER_FRAMES frames written"
fi

if [ -f ../../cross-platform-tests/results/direct_cpp_reader.json ]; then
    READER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/direct_cpp_reader.json'))['frames_read'])" 2>/dev/null || echo "?")
    READER_ERRORS=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/direct_cpp_reader.json'))['verification_errors'])" 2>/dev/null || echo "?")
    echo "Reader: $READER_FRAMES frames read, $READER_ERRORS verification errors"
fi

# Check for wrap-around success
if [ "$WRITER_FRAMES" = "$READER_FRAMES" ] && [ "$READER_ERRORS" = "0" ]; then
    echo ""
    echo -e "${GREEN}Test PASSED - Wrap-around handled correctly${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}Test FAILED - Check logs for wrap-around issues${NC}"
    # Show last few lines of logs for debugging
    echo ""
    echo "Last lines of writer log:"
    tail -20 ../../cross-platform-tests/results/direct_csharp_writer.json
    echo ""
    echo "Last lines of reader log:"
    tail -20 ../../cross-platform-tests/results/direct_cpp_reader.json
    exit 1
fi