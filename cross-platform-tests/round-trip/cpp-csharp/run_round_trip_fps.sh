#!/bin/bash

# C++ to C# Round-Trip Test with FPS pacing
# C++ writes frames at specified FPS, C# reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="cpp_csharp_fps_$$"
FRAME_COUNT=300  # 10 seconds at 30 FPS
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420 Full HD: 3,110,400 bytes
HEADER_SIZE=16
TOTAL_FRAME_SIZE=$((FRAME_SIZE + HEADER_SIZE))
METADATA_SIZE=4096
BUFFER_SIZE=$((256 * 1024 * 1024)) # 256MB
TARGET_FPS=30

echo "======================================"
echo "C++ → C# Round-Trip Test (FPS Paced)"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT at $TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB (YUV420 Full HD)"
echo "Buffer size: $((BUFFER_SIZE / 1024 / 1024))MB"
echo "Expected duration: $((FRAME_COUNT / TARGET_FPS)) seconds"
echo ""

# Calculate delay between frames in milliseconds
DELAY_MS=$((1000 / TARGET_FPS))

# Clean up any previous results
rm -f "$SCRIPT_DIR/reader_output.txt" "$SCRIPT_DIR/writer_output.txt"
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true
rm -f /dev/shm/sem.*${BUFFER_NAME}* 2>/dev/null || true

# Start C# reader in background
echo "[1/3] Starting C# reader..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

# Set longer timeout since we're writing at FPS rate
READER_TIMEOUT=$((FRAME_COUNT / TARGET_FPS * 2000))  # 2x expected duration in ms

./ZeroBuffer.CrossPlatform reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --metadata-size $METADATA_SIZE \
    --buffer-size $BUFFER_SIZE \
    --timeout-ms $READER_TIMEOUT \
    --verbose \
    > "$SCRIPT_DIR/reader_output.txt" 2>&1 &
READER_PID=$!

# Give reader time to create buffer
echo "      Waiting for buffer creation..."
sleep 3

# Check if reader is still running
if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader died during startup"
    cat "$SCRIPT_DIR/reader_output.txt"
    exit 1
fi

# Run C++ writer with FPS pacing
echo "[2/3] Starting C++ writer at $TARGET_FPS FPS..."
echo "      This will take approximately $((FRAME_COUNT / TARGET_FPS)) seconds..."
cd "$PROJECT_ROOT/cpp/build"

START_TIME=$(date +%s)

./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms $DELAY_MS \
    --verbose \
    > "$SCRIPT_DIR/writer_output.txt" 2>&1

WRITER_EXIT=$?
END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "      Writer completed in $DURATION seconds"

# Wait for reader to complete (should be quick since writer is done)
echo "[3/3] Waiting for reader to finish..."
READER_EXIT=1
for i in {1..10}; do
    if ! kill -0 $READER_PID 2>/dev/null; then
        wait $READER_PID
        READER_EXIT=$?
        break
    fi
    sleep 1
done

# Kill reader if still running
if kill -0 $READER_PID 2>/dev/null; then
    echo "      Reader still running, killing..."
    kill $READER_PID 2>/dev/null || true
fi

echo ""
echo "======================================"
echo "Results:"
echo "======================================"

# Parse writer results
WRITER_SUCCESS=false
if [ $WRITER_EXIT -eq 0 ]; then
    WRITER_FRAMES=$(grep -oP "wrote \K\d+" "$SCRIPT_DIR/writer_output.txt" | tail -1 || echo "0")
    WRITER_THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/writer_output.txt" || echo "N/A")
    WRITER_DURATION=$(grep -oP "in \K[0-9.]+" "$SCRIPT_DIR/writer_output.txt" | tail -1 || echo "N/A")
    
    if [ "$WRITER_FRAMES" = "$FRAME_COUNT" ]; then
        echo "✓ Writer: Successfully wrote $WRITER_FRAMES frames in ${WRITER_DURATION}s"
        echo "          Throughput: $WRITER_THROUGHPUT MB/s"
        echo "          Effective FPS: $(echo "scale=2; $WRITER_FRAMES / $WRITER_DURATION" | bc)"
        WRITER_SUCCESS=true
    else
        echo "✗ Writer: Only wrote $WRITER_FRAMES out of $FRAME_COUNT frames"
    fi
else
    echo "✗ Writer: Failed with exit code $WRITER_EXIT"
fi

# Parse reader results
READER_SUCCESS=false
READER_FRAMES=$(grep -oP "read \K\d+" "$SCRIPT_DIR/reader_output.txt" | tail -1 || echo "0")
READER_ERRORS=$(grep -oP "Verification errors: \K\d+" "$SCRIPT_DIR/reader_output.txt" || echo "N/A")
READER_THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/reader_output.txt" || echo "N/A")

if [ "$READER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_ERRORS" = "0" ]; then
    echo "✓ Reader: Successfully read $READER_FRAMES frames with 0 errors"
    echo "          Throughput: $READER_THROUGHPUT MB/s"
    READER_SUCCESS=true
else
    echo "✗ Reader: Read $READER_FRAMES frames with $READER_ERRORS errors"
    
    # Show why reader stopped
    if [ "$READER_FRAMES" -lt "$FRAME_COUNT" ]; then
        echo ""
        echo "Reader stopped early. Last few lines:"
        tail -5 "$SCRIPT_DIR/reader_output.txt"
    fi
fi

# Check for wrap-around
echo ""
echo "Wrap-around Analysis:"
# Calculate how many times we should wrap
# Buffer: 256MB, Frame: ~3.1MB, so ~82 frames fit before wrap
EXPECTED_WRAPS=$((FRAME_COUNT / 82))
WRAP_COUNT=$(grep -c "wrap" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null || echo "0")

if [ $EXPECTED_WRAPS -gt 0 ]; then
    if [ $WRAP_COUNT -gt 0 ]; then
        echo "✓ Buffer wrap-around detected ($WRAP_COUNT occurrences, expected ~$EXPECTED_WRAPS)"
        grep -i "wrap" "$SCRIPT_DIR/reader_output.txt" | head -3
    else
        echo "✗ No wrap-around detected (expected ~$EXPECTED_WRAPS)"
    fi
else
    echo "  Not enough frames to cause wrap-around"
fi

# Overall result
echo ""
echo "======================================"
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    echo "✓ TEST PASSED"
    EXIT_CODE=0
else
    echo "✗ TEST FAILED"
    EXIT_CODE=1
fi
echo "======================================"

# Cleanup
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true
rm -f /dev/shm/sem.*${BUFFER_NAME}* 2>/dev/null || true

exit $EXIT_CODE