#!/bin/bash

# Simple wrap-around test with smaller buffer
# C# reader creates 100MB buffer, which holds ~33 frames

BUFFER_NAME="simple_wrap_test"
FRAME_COUNT=40  # Enough to cause wrap-around in 100MB buffer
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes
FRAMES_IN_100MB=$((100 * 1024 * 1024 / FRAME_SIZE))

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Simple Wrap-Around Test (100MB buffer)${NC}"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes"
echo "Buffer capacity: ~$FRAMES_IN_100MB frames"
echo "-----------------------------------"

# Clean up
rm -f /dev/shm/$BUFFER_NAME* 2>/dev/null
rm -f /dev/shm/sem.sem-* 2>/dev/null
mkdir -p ../results

# Test: C++ Writer → C# Reader
echo ""
echo -e "${YELLOW}C++ Writer → C# Reader with verification${NC}"

# Start C# reader (creates 100MB buffer)
cd ../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --verbose \
    --json-output \
    > ../cross-platform-tests/results/simple_wrap_reader.json 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 3

# Start C++ writer
cd ../cpp/build
./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms 50 \
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/simple_wrap_writer.json 2>&1 &
WRITER_PID=$!

# Wait for writer
wait $WRITER_PID
WRITER_EXIT=$?

# Give reader time to finish
echo "Waiting for reader to finish processing..."
sleep 10

if kill -0 $READER_PID 2>/dev/null; then
    kill $READER_PID
fi
wait $READER_PID 2>/dev/null
READER_EXIT=$?

# Analyze results
echo ""
echo -e "${YELLOW}Results:${NC}"
echo "Writer exit: $WRITER_EXIT, Reader exit: $READER_EXIT"

# Parse results
if [ -f ../../cross-platform-tests/results/simple_wrap_writer.json ]; then
    WRITER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/simple_wrap_writer.json')).get('frames_written', 0))" 2>/dev/null || echo "0")
    echo "C++ Writer: $WRITER_FRAMES frames written"
fi

if [ -f ../../cross-platform-tests/results/simple_wrap_reader.json ]; then
    READER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/simple_wrap_reader.json')).get('FramesRead', 0))" 2>/dev/null || echo "0")
    READER_ERRORS=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/simple_wrap_reader.json')).get('VerificationErrors', 0))" 2>/dev/null || echo "0")
    echo "C# Reader: $READER_FRAMES frames read, $READER_ERRORS verification errors"
fi

# Check success
if [ "$WRITER_FRAMES" = "$READER_FRAMES" ] && [ "$READER_ERRORS" = "0" ] && [ "$WRITER_FRAMES" = "$FRAME_COUNT" ]; then
    echo ""
    echo -e "${GREEN}✓ SUCCESS: Wrap-around works correctly!${NC}"
    echo "  All $FRAME_COUNT frames transferred with correct data"
    exit 0
else
    echo ""
    echo -e "${RED}✗ FAILURE: Wrap-around test failed${NC}"
    if [ "$WRITER_FRAMES" != "$READER_FRAMES" ]; then
        echo "  Frame count mismatch: wrote $WRITER_FRAMES, read $READER_FRAMES"
    fi
    if [ "$READER_ERRORS" != "0" ]; then
        echo "  Data verification failed: $READER_ERRORS errors"
    fi
    
    # Show logs
    echo ""
    echo "Writer output:"
    cat ../../cross-platform-tests/results/simple_wrap_writer.json
    echo ""
    echo "Reader output (last 50 lines):"
    tail -50 ../../cross-platform-tests/results/simple_wrap_reader.json
    exit 1
fi