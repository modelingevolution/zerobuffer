#!/bin/bash

# C# → C++ Round-Trip Test
# Tests C# writer with C++ reader

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="csharp_to_cpp_$$"
FRAME_COUNT=300
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420: 3,110,400 bytes
BUFFER_SIZE=$((256 * 1024 * 1024))   # 256MB
TARGET_FPS=30

echo "======================================"
echo "C# → C++ Round-Trip Test"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT at $TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo "Buffer size: $((BUFFER_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true

# Step 1: Start C++ reader (creates buffer)
echo "Starting C++ reader..."
cd "$PROJECT_ROOT/cpp/build"

# Use a very long timeout (60 seconds) to ensure reader doesn't exit early
./tests/zerobuffer-test-reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify sequential \
    --timeout-ms 60000 \
    --verbose 2>&1 | tee "$SCRIPT_DIR/reader.log" &
READER_PID=$!

# Wait for buffer creation
echo "Waiting for buffer creation..."
sleep 2

if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader exited early. Checking logs..."
    tail -20 "$SCRIPT_DIR/reader.log"
    exit 1
fi

# Step 2: Start C# writer (connects to buffer)
echo "Starting C# writer..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

./ZeroBuffer.CrossPlatform writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms $((1000 / TARGET_FPS)) \
    --verbose 2>&1 | tee "$SCRIPT_DIR/writer.log"

WRITER_EXIT=$?

# Wait for reader
echo "Waiting for reader to finish..."
wait $READER_PID
READER_EXIT=$?

# Parse results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"

# Check if reader actually read frames
FRAMES_READ=$(grep -oP "read \K\d+" "$SCRIPT_DIR/reader.log" | tail -1 || echo "0")
VERIFICATION_ERRORS=$(grep -oP "Verification errors: \K\d+" "$SCRIPT_DIR/reader.log" || echo "N/A")

echo "Writer exit code: $WRITER_EXIT"
echo "Reader exit code: $READER_EXIT"
echo "Frames read: $FRAMES_READ"
echo "Verification errors: $VERIFICATION_ERRORS"

# Check for wrap-around
if grep -q "wrap" "$SCRIPT_DIR/reader.log"; then
    echo ""
    echo "Wrap-around detected:"
    grep -i "wrap" "$SCRIPT_DIR/reader.log" | head -3
fi

# Clean up logs
rm -f "$SCRIPT_DIR/reader.log" "$SCRIPT_DIR/writer.log"

if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ] && [ "$FRAMES_READ" = "$FRAME_COUNT" ]; then
    echo ""
    echo "✓ TEST PASSED"
    exit 0
else
    echo ""
    echo "✗ TEST FAILED"
    exit 1
fi