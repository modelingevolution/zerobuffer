#!/bin/bash

# Test C# and C++ interoperability directly
# This verifies the wrap-around issue at frame 87

BUFFER_NAME="csharp_cpp_test"
FRAME_COUNT=90  # Just past wrap-around (86 frames fit)
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}C# Reader → C++ Writer Direct Test${NC}"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes"
echo "-----------------------------------"

# Clean up
rm -f /dev/shm/*$BUFFER_NAME* 2>/dev/null
rm -f /dev/shm/sem.*$BUFFER_NAME* 2>/dev/null
mkdir -p ../results

# Start C# reader (creates buffer)
echo -e "${YELLOW}Starting C# reader...${NC}"
cd ../../csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0
./ZeroBuffer.CrossPlatform reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --verbose \
    > ../../../../../cross-platform-tests/results/direct_csharp_reader_output.log 2>&1 &
READER_PID=$!

# Wait for buffer creation
sleep 3

# Check if buffer was created
echo "Checking shared memory files:"
ls -la /dev/shm/ | grep "$BUFFER_NAME" || echo "No buffer files found"

# Start C++ writer
echo -e "${YELLOW}Starting C++ writer...${NC}"
cd ../../../../../cpp/build
./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    > ../../cross-platform-tests/results/direct_cpp_writer_output.log 2>&1

WRITER_EXIT=$?
echo "Writer exit code: $WRITER_EXIT"

# Give reader time to finish
sleep 5

# Kill reader if still running
if kill -0 $READER_PID 2>/dev/null; then
    echo "Killing reader..."
    kill $READER_PID
fi

# Show outputs
echo ""
echo -e "${YELLOW}Writer output:${NC}"
tail -30 ../../cross-platform-tests/results/direct_cpp_writer_output.log

echo ""
echo -e "${YELLOW}Reader output:${NC}"
tail -30 ../../cross-platform-tests/results/direct_csharp_reader_output.log

# Clean up
rm -f /dev/shm/*$BUFFER_NAME* 2>/dev/null