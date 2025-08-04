#!/bin/bash

# C++ → C# Round-Trip Test
# Tests C++ writer with C# reader

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="cpp_to_csharp_$$"
FRAME_COUNT=300
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420: 3,110,400 bytes
BUFFER_SIZE=$((256 * 1024 * 1024))   # 256MB
TARGET_FPS=30

echo "======================================"
echo "C++ → C# Round-Trip Test"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT at $TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo "Buffer size: $((BUFFER_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true

# Step 1: Start C# reader (creates buffer)
echo "Starting C# reader..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

./ZeroBuffer.CrossPlatform reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --buffer-size $BUFFER_SIZE \
    --timeout-ms 20000 \
    --verbose &
READER_PID=$!

# Wait for buffer creation
sleep 2

# Step 2: Start C++ writer (connects to buffer)
echo "Starting C++ writer..."
cd "$PROJECT_ROOT/cpp/build"

./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms $((1000 / TARGET_FPS)) \
    --verbose

WRITER_EXIT=$?

# Wait for reader
wait $READER_PID
READER_EXIT=$?

# Results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"
echo "Writer exit code: $WRITER_EXIT"
echo "Reader exit code: $READER_EXIT"

if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo "✓ TEST PASSED"
    exit 0
else
    echo "✗ TEST FAILED"
    exit 1
fi