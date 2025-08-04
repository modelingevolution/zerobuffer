#!/bin/bash

# Debug wrap-around issue with detailed logging
# Tests exactly around frame 86/87 boundary

BUFFER_NAME="debug_wrap_test"
FRAME_COUNT=88  # Just past the wrap-around point
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Debug Wrap-Around Test${NC}"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT (wrap at frame 87)"
echo "Frame size: $FRAME_SIZE bytes"
echo "-----------------------------------"

# Clean up
rm -f /dev/shm/*$BUFFER_NAME* 2>/dev/null
rm -f /dev/shm/sem.*$BUFFER_NAME* 2>/dev/null
mkdir -p ../results

# Test: C# Reader with logging → C++ Writer
echo -e "${YELLOW}Starting C# reader with debug logging...${NC}"
cd ../../csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0

# Set logging to debug level
export DOTNET_LOG_LEVEL=Debug

./ZeroBuffer.CrossPlatform reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --verbose \
    > ../../../../../cross-platform-tests/results/debug_wrap_reader.log 2>&1 &
READER_PID=$!

# Wait for buffer creation
sleep 3

# Start C++ writer
echo -e "${YELLOW}Starting C++ writer...${NC}"
cd ../../../../../cpp/build
./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms 100 \
    --verbose \
    > ../../cross-platform-tests/results/debug_wrap_writer.log 2>&1

WRITER_EXIT=$?
echo "Writer exit code: $WRITER_EXIT"

# Give reader time to finish
sleep 3

# Kill reader if still running
if kill -0 $READER_PID 2>/dev/null; then
    echo "Killing reader..."
    kill $READER_PID
fi

# Extract key information
echo ""
echo -e "${YELLOW}Analysis:${NC}"

# Show frames around wrap boundary
echo ""
echo "Writer log around frame 85-87:"
grep -E "frame (8[567]|88)" ../../cross-platform-tests/results/debug_wrap_writer.log || echo "No specific frame logs found"

echo ""
echo "Reader log showing wrap-around handling:"
grep -E -A3 -B3 "wrap|Wrap|frame.*8[567]|Frame.*8[567]" ../../cross-platform-tests/results/debug_wrap_reader.log | head -50 || echo "No wrap logs found"

echo ""
echo "Reader final summary:"
tail -20 ../../cross-platform-tests/results/debug_wrap_reader.log

# Check if wrap was successful
WRITER_FRAMES=$(grep -oP "wrote \K\d+" ../../cross-platform-tests/results/debug_wrap_writer.log | tail -1 || echo "0")
READER_FRAMES=$(grep -oP "[Rr]ead \K\d+" ../../cross-platform-tests/results/debug_wrap_reader.log | tail -1 || echo "0")

echo ""
echo -e "${YELLOW}Summary:${NC}"
echo "Writer wrote: $WRITER_FRAMES frames"
echo "Reader read: $READER_FRAMES frames"

if [ "$WRITER_FRAMES" = "$READER_FRAMES" ] && [ "$WRITER_FRAMES" = "$FRAME_COUNT" ]; then
    echo -e "${GREEN}✓ Wrap-around handled correctly${NC}"
else
    echo -e "${RED}✗ Wrap-around issue detected${NC}"
    echo "Expected $FRAME_COUNT frames, but reader only got $READER_FRAMES"
fi

# Clean up
rm -f /dev/shm/*$BUFFER_NAME* 2>/dev/null