#!/bin/bash

# Full C++ → C# → C++ Round-Trip Test using existing test programs

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_A="cpp_to_csharp_$$"
BUFFER_B="csharp_to_cpp_$$"
FRAME_COUNT=300
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420 Full HD
PATTERN="sequential"

echo "======================================"
echo "C++ → C# → C++ Full Round-Trip Test"
echo "======================================"
echo "Buffer A (C++ → C#): $BUFFER_A"
echo "Buffer B (C# → C++): $BUFFER_B"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_A}* 2>/dev/null || true
rm -f /dev/shm/*${BUFFER_B}* 2>/dev/null || true
rm -f "$SCRIPT_DIR"/*.log 2>/dev/null || true

# Step 1: Start C# relay (it creates buffer A as Reader)
echo "Starting C# relay..."
cd "$PROJECT_ROOT/csharp"
dotnet run --project ZeroBuffer.CrossPlatform -- relay "$BUFFER_A" "$BUFFER_B" \
    --frames $FRAME_COUNT \
    --verbose \
    --log-interval 10 \
    2>&1 | tee "$SCRIPT_DIR/relay.log" &
RELAY_PID=$!

# Wait for relay to initialize
sleep 3

# Check if relay is still running
if ! kill -0 $RELAY_PID 2>/dev/null; then
    echo "ERROR: Relay died during startup"
    cat "$SCRIPT_DIR/relay.log"
    exit 1
fi

# Step 2: Start C++ reader (creates buffer B)
echo ""
echo "Starting C++ reader..."
cd "$PROJECT_ROOT/cpp/build"
./tests/zerobuffer-test-reader "$BUFFER_B" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify $PATTERN \
    --verbose \
    2>&1 | tee "$SCRIPT_DIR/reader.log" &
READER_PID=$!

# Wait for reader to create buffer
sleep 2

# Check if reader is still running
if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader died during startup"
    cat "$SCRIPT_DIR/reader.log"
    kill $RELAY_PID 2>/dev/null || true
    exit 1
fi

# Step 3: Start C++ writer (connects to buffer A)
echo ""
echo "Starting C++ writer..."
./tests/zerobuffer-test-writer "$BUFFER_A" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern $PATTERN \
    --verbose \
    --delay-us 33333 \
    2>&1 | tee "$SCRIPT_DIR/writer.log"
WRITER_EXIT=$?

# Wait for reader and relay to finish
echo ""
echo "Waiting for reader and relay to finish..."
wait $READER_PID
READER_EXIT=$?

# Give relay time to finish
sleep 1
kill $RELAY_PID 2>/dev/null || true
wait $RELAY_PID
RELAY_EXIT=$?

# Parse results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"

# Extract metrics
WRITER_FRAMES=$(grep -oP "frames_written.*: \K\d+" "$SCRIPT_DIR/writer.log" || echo "0")
READER_FRAMES=$(grep -oP "frames_read.*: \K\d+" "$SCRIPT_DIR/reader.log" || echo "0")
READER_ERRORS=$(grep -oP "verification_errors.*: \K\d+" "$SCRIPT_DIR/reader.log" || echo "0")
RELAY_IN=$(grep -oP "Frames read: \K\d+" "$SCRIPT_DIR/relay.log" | tail -1 || echo "0")
RELAY_OUT=$(grep -oP "Frames written: \K\d+" "$SCRIPT_DIR/relay.log" | tail -1 || echo "0")

echo "Exit codes:"
echo "  Writer: $WRITER_EXIT"
echo "  Relay: $RELAY_EXIT"
echo "  Reader: $READER_EXIT"
echo ""
echo "Frame counts:"
echo "  Writer wrote: $WRITER_FRAMES"
echo "  Relay read: $RELAY_IN"
echo "  Relay wrote: $RELAY_OUT"
echo "  Reader read: $READER_FRAMES"
echo "  Reader errors: $READER_ERRORS"

# Check for wrap-around
echo ""
echo "Wrap-around events:"
grep -i "wrap" "$SCRIPT_DIR/writer.log" 2>/dev/null | head -3 || echo "  Writer: no wraps detected"
grep -i "wrap" "$SCRIPT_DIR/relay.log" 2>/dev/null | head -3 || echo "  Relay: no wraps detected"
grep -i "wrap" "$SCRIPT_DIR/reader.log" 2>/dev/null | head -3 || echo "  Reader: no wraps detected"

# Determine success
if [ "$WRITER_EXIT" -eq 0 ] && [ "$READER_EXIT" -eq 0 ] && [ "$WRITER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_ERRORS" = "0" ]; then
    echo ""
    echo "✓ TEST PASSED"
    EXIT_CODE=0
else
    echo ""
    echo "✗ TEST FAILED"
    EXIT_CODE=1
fi

# Cleanup
rm -f /dev/shm/*${BUFFER_A}* 2>/dev/null || true
rm -f /dev/shm/*${BUFFER_B}* 2>/dev/null || true

exit $EXIT_CODE