#!/bin/bash

# C++ → C# Relay → C++ Test
# Tests full round-trip with C# relay in the middle

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_A="relay_input_$$"
BUFFER_B="relay_output_$$"
FRAME_COUNT=300
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420: 3,110,400 bytes
BUFFER_SIZE=$((256 * 1024 * 1024))   # 256MB
TARGET_FPS=30

echo "======================================"
echo "C++ → C# Relay → C++ Test"
echo "======================================"
echo "Input buffer: $BUFFER_A"
echo "Output buffer: $BUFFER_B"
echo "Frames: $FRAME_COUNT at $TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo "Buffer size: $((BUFFER_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_A}* /dev/shm/*${BUFFER_B}* 2>/dev/null || true

# Step 1: Start C# relay (creates input buffer, connects to output)
echo "Starting C# relay..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

./ZeroBuffer.CrossPlatform relay "$BUFFER_A" "$BUFFER_B" \
    --buffer-size $BUFFER_SIZE \
    --output-buffer-size $BUFFER_SIZE \
    --verbose &
RELAY_PID=$!

# Wait for relay initialization
sleep 2

# Step 2: Start C++ reader (creates output buffer)
echo "Starting C++ reader..."
cd "$PROJECT_ROOT/cpp/build"

./tests/zerobuffer-test-reader "$BUFFER_B" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify sequential \
    --verbose &
READER_PID=$!

# Wait for reader initialization
sleep 2

# Step 3: Start C++ writer (connects to input buffer)
echo "Starting C++ writer..."
./tests/zerobuffer-test-writer "$BUFFER_A" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-us $((1000000 / TARGET_FPS)) \
    --verbose

WRITER_EXIT=$?

# Wait for reader and relay
wait $READER_PID
READER_EXIT=$?

# Stop relay
kill $RELAY_PID 2>/dev/null || true
wait $RELAY_PID 2>/dev/null
RELAY_EXIT=$?

# Results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"
echo "Writer exit code: $WRITER_EXIT"
echo "Relay exit code: $RELAY_EXIT"
echo "Reader exit code: $READER_EXIT"

if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo "✓ TEST PASSED"
    exit 0
else
    echo "✗ TEST FAILED"
    exit 1
fi