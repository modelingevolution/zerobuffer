#!/bin/bash

# Detailed wrap-around verification test
# Tests data integrity across wrap-around boundary between C++ and C#

BUFFER_NAME="verify_wrap_test"
FRAME_COUNT=95  # Test frames 85, 86, 87, 88 (before and after wrap)
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Wrap-Around Data Verification Test${NC}"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes"
echo "Critical frames: 85, 86 (last before wrap), 87, 88 (first after wrap)"
echo "-----------------------------------"

# Clean up
rm -f /dev/shm/$BUFFER_NAME* 2>/dev/null
rm -f /dev/shm/sem.sem-* 2>/dev/null
mkdir -p ../results

# Test: C++ Writer → C# Reader with detailed verification
echo ""
echo -e "${YELLOW}Running C++ Writer → C# Reader with sequential pattern verification${NC}"

# Start C# reader with sequential pattern verification and checksum
cd ../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --create \
    --buffer-size $((256 * 1024 * 1024)) \
    --verify sequential \
    --checksum \
    --verbose \
    --json-output \
    > ../cross-platform-tests/results/verify_wrap_csharp_reader.json 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 3

# Start C++ writer with sequential pattern
cd ../cpp/build
./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/verify_wrap_cpp_writer.json 2>&1 &
WRITER_PID=$!

# Wait for writer to complete
wait $WRITER_PID
WRITER_EXIT=$?

# Give reader more time to process all frames
echo "Waiting for reader to process all frames..."
sleep 10

# Check if reader is still running
if kill -0 $READER_PID 2>/dev/null; then
    echo "Reader still running after 10 seconds, sending SIGTERM..."
    kill $READER_PID
fi

wait $READER_PID 2>/dev/null
READER_EXIT=$?

echo "Writer exit: $WRITER_EXIT, Reader exit: $READER_EXIT"

# Analyze results
echo ""
echo -e "${YELLOW}Results Analysis:${NC}"

# Check writer results
if [ -f ../../cross-platform-tests/results/verify_wrap_cpp_writer.json ]; then
    WRITER_FRAMES=$(python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_cpp_writer.json')); print(data.get('frames_written', 0))" 2>/dev/null || echo "0")
    WRITER_ERRORS=$(python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_cpp_writer.json')); print(len(data.get('errors', [])))" 2>/dev/null || echo "0")
    echo "C++ Writer: $WRITER_FRAMES frames written, $WRITER_ERRORS errors"
    
    # Show any writer errors
    if [ "$WRITER_ERRORS" -gt 0 ]; then
        echo "Writer errors:"
        python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_cpp_writer.json')); [print(f'  - {e}') for e in data.get('errors', [])]" 2>/dev/null
    fi
fi

# Check reader results
if [ -f ../../cross-platform-tests/results/verify_wrap_csharp_reader.json ]; then
    READER_FRAMES=$(python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_csharp_reader.json')); print(data.get('FramesRead', 0))" 2>/dev/null || echo "0")
    READER_VERIFY_ERRORS=$(python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_csharp_reader.json')); print(data.get('VerificationErrors', 0))" 2>/dev/null || echo "0")
    READER_ERRORS=$(python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_csharp_reader.json')); print(len(data.get('Errors', [])))" 2>/dev/null || echo "0")
    echo "C# Reader: $READER_FRAMES frames read, $READER_VERIFY_ERRORS verification errors, $READER_ERRORS other errors"
    
    # Show verification errors
    if [ "$READER_VERIFY_ERRORS" -gt 0 ]; then
        echo "Data verification FAILED - pattern mismatch detected!"
    fi
    
    # Show any reader errors
    if [ "$READER_ERRORS" -gt 0 ]; then
        echo "Reader errors:"
        python3 -c "import json; data=json.load(open('../../cross-platform-tests/results/verify_wrap_csharp_reader.json')); [print(f'  - {e}') for e in data.get('Errors', [])]" 2>/dev/null
    fi
    
    # Show checksums for critical frames
    echo ""
    echo "Checksums for critical frames (around wrap boundary):"
    python3 -c "
import json
data = json.load(open('../../cross-platform-tests/results/verify_wrap_csharp_reader.json'))
checksums = data.get('Checksums', [])
critical_frames = [84, 85, 86, 87, 88, 89]
for cs in checksums:
    if cs.get('Frame') in critical_frames:
        print(f\"  Frame {cs['Frame']}: {cs['Checksum']}\")
" 2>/dev/null || echo "  (No checksum data available)"
fi

# Final verdict
echo ""
if [ "$WRITER_FRAMES" = "$READER_FRAMES" ] && [ "$READER_VERIFY_ERRORS" = "0" ] && [ "$WRITER_EXIT" = "0" ] && [ "$READER_EXIT" = "0" ]; then
    echo -e "${GREEN}✓ PASS: Wrap-around test successful!${NC}"
    echo "  - All $WRITER_FRAMES frames written and read correctly"
    echo "  - Data integrity verified (sequential pattern matched)"
    echo "  - No errors during wrap-around at frame 87"
    exit 0
else
    echo -e "${RED}✗ FAIL: Wrap-around test failed!${NC}"
    if [ "$WRITER_FRAMES" != "$READER_FRAMES" ]; then
        echo "  - Frame count mismatch: wrote $WRITER_FRAMES, read $READER_FRAMES"
    fi
    if [ "$READER_VERIFY_ERRORS" != "0" ]; then
        echo "  - Data corruption detected: $READER_VERIFY_ERRORS frames with wrong pattern"
    fi
    
    # Show detailed logs around wrap boundary
    echo ""
    echo "Detailed logs around frame 86/87:"
    echo "Writer logs:"
    grep -A2 -B2 "frame 8[567]" ../../cross-platform-tests/results/verify_wrap_cpp_writer.json 2>/dev/null || echo "(No specific frame logs)"
    echo ""
    echo "Reader logs:"
    grep -A2 -B2 "Frame 8[567]" ../../cross-platform-tests/results/verify_wrap_csharp_reader.json 2>/dev/null || echo "(No specific frame logs)"
    
    exit 1
fi