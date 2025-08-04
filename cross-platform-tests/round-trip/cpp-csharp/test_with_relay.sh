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
rm -f "$SCRIPT_DIR"/*.log 2>/dev/null || true

# Step 1: Start C# relay (creates input buffer as Reader)
echo "Starting C# relay..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

./ZeroBuffer.CrossPlatform relay "$BUFFER_A" "$BUFFER_B" \
    --frames $FRAME_COUNT \
    --create-output \
    --buffer-size $BUFFER_SIZE \
    --verbose 2>&1 | tee "$SCRIPT_DIR/relay.log" &
RELAY_PID=$!

# Wait for relay initialization
echo "Waiting for relay to initialize..."
sleep 3

# Check if relay is still running
if ! kill -0 $RELAY_PID 2>/dev/null; then
    echo "ERROR: Relay died during startup"
    tail -20 "$SCRIPT_DIR/relay.log"
    exit 1
fi

# Step 2: Start C++ reader (creates output buffer)
echo "Starting C++ reader..."
cd "$PROJECT_ROOT/cpp/build"

./tests/zerobuffer-test-reader "$BUFFER_B" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify sequential \
    --timeout-ms 30000 \
    --verbose 2>&1 | tee "$SCRIPT_DIR/reader.log" &
READER_PID=$!

# Wait for reader initialization
sleep 2

# Check if reader is still running
if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader died during startup"
    tail -20 "$SCRIPT_DIR/reader.log"
    kill $RELAY_PID 2>/dev/null || true
    exit 1
fi

# Step 3: Start C++ writer (connects to input buffer)
echo "Starting C++ writer..."
./tests/zerobuffer-test-writer "$BUFFER_A" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms $((1000 / TARGET_FPS)) \
    --verbose 2>&1 | tee "$SCRIPT_DIR/writer.log"

WRITER_EXIT=$?

# Wait for reader to finish
echo "Waiting for reader to finish..."
wait $READER_PID
READER_EXIT=$?

# Stop relay
echo "Stopping relay..."
kill $RELAY_PID 2>/dev/null || true
wait $RELAY_PID 2>/dev/null
RELAY_EXIT=$?

# Parse results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"

# Extract metrics from text output (not JSON)
WRITER_FRAMES=$(grep -oP "wrote \K\d+" "$SCRIPT_DIR/writer.log" | tail -1 || echo "0")
READER_FRAMES=$(grep -oP "read \K\d+" "$SCRIPT_DIR/reader.log" | tail -1 || echo "0")
READER_ERRORS=$(grep -oP "Verification errors: \K\d+" "$SCRIPT_DIR/reader.log" || echo "0")
RELAY_FRAMES=$(grep -oP "Progress: \K\d+" "$SCRIPT_DIR/relay.log" | tail -1 || echo "0")

echo "Exit codes:"
echo "  Writer: $WRITER_EXIT"
echo "  Relay: $RELAY_EXIT"
echo "  Reader: $READER_EXIT"
echo ""
echo "Frame counts:"
echo "  Writer wrote: $WRITER_FRAMES"
echo "  Relay processed: $RELAY_FRAMES"
echo "  Reader read: $READER_FRAMES"
echo "  Reader errors: $READER_ERRORS"

# Check for wrap-around
echo ""
echo "Wrap-around events:"
if grep -q "wrap" "$SCRIPT_DIR/writer.log" 2>/dev/null; then
    echo "  Writer:"
    grep -i "wrap" "$SCRIPT_DIR/writer.log" | head -3
fi
if grep -q "wrap" "$SCRIPT_DIR/relay.log" 2>/dev/null; then
    echo "  Relay:"
    grep -i "wrap" "$SCRIPT_DIR/relay.log" | head -3
fi
if grep -q "wrap" "$SCRIPT_DIR/reader.log" 2>/dev/null; then
    echo "  Reader:"
    grep -i "wrap" "$SCRIPT_DIR/reader.log" | head -3
fi

# Clean up logs
rm -f "$SCRIPT_DIR"/*.log

# Determine success
if [ "$WRITER_EXIT" -eq 0 ] && [ "$READER_EXIT" -eq 0 ] && [ "$WRITER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_ERRORS" = "0" ]; then
    echo ""
    echo "✓ TEST PASSED"
    exit 0
else
    echo ""
    echo "✗ TEST FAILED"
    exit 1
fi