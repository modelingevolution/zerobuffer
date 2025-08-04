#!/bin/bash

# C++ to C# Round-Trip Test
# C++ writes frames, C# reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="cpp_csharp_test_$$"
FRAME_COUNT=1000
FRAME_SIZE=1024  # Just data size, header will be added
METADATA_SIZE=4096
BUFFER_SIZE=$((256 * 1024 * 1024)) # 256MB

echo "======================================"
echo "C++ → C# Round-Trip Test"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $FRAME_SIZE bytes"
echo "Buffer size: $((BUFFER_SIZE / 1024 / 1024))MB"
echo ""

# Clean up any previous results
rm -f "$SCRIPT_DIR/reader_output.txt" "$SCRIPT_DIR/writer_output.txt"

# Start C# reader in background
echo "[1/3] Starting C# reader..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

./ZeroBuffer.CrossPlatform reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --metadata-size $METADATA_SIZE \
    --buffer-size $BUFFER_SIZE \
    > "$SCRIPT_DIR/reader_output.txt" 2>&1 &
READER_PID=$!

# Give reader time to create buffer
echo "      Waiting for buffer creation..."
sleep 3

# Run C++ writer
echo "[2/3] Starting C++ writer..."
cd "$PROJECT_ROOT/cpp/build"
./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    > "$SCRIPT_DIR/writer_output.txt" 2>&1

WRITER_EXIT=$?

# Wait for reader to complete
echo "[3/3] Waiting for reader to finish..."
READER_EXIT=1
if wait $READER_PID; then
    READER_EXIT=0
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
    
    if [ "$WRITER_FRAMES" = "$FRAME_COUNT" ]; then
        echo "✓ Writer: Successfully wrote $WRITER_FRAMES frames ($WRITER_THROUGHPUT MB/s)"
        WRITER_SUCCESS=true
    else
        echo "✗ Writer: Only wrote $WRITER_FRAMES out of $FRAME_COUNT frames"
    fi
else
    echo "✗ Writer: Failed with exit code $WRITER_EXIT"
    tail -10 "$SCRIPT_DIR/writer_output.txt"
fi

# Parse reader results
READER_SUCCESS=false
if [ $READER_EXIT -eq 0 ]; then
    READER_FRAMES=$(grep -oP "read \K\d+" "$SCRIPT_DIR/reader_output.txt" | tail -1 || echo "0")
    READER_ERRORS=$(grep -oP "Verification errors: \K\d+" "$SCRIPT_DIR/reader_output.txt" || echo "N/A")
    READER_THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/reader_output.txt" || echo "N/A")
    
    if [ "$READER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_ERRORS" = "0" ]; then
        echo "✓ Reader: Successfully read $READER_FRAMES frames with 0 errors ($READER_THROUGHPUT MB/s)"
        READER_SUCCESS=true
    else
        echo "✗ Reader: Read $READER_FRAMES frames with $READER_ERRORS errors"
    fi
else
    echo "✗ Reader: Failed with exit code $READER_EXIT"
    tail -10 "$SCRIPT_DIR/reader_output.txt"
fi

# Check for wrap-around
echo ""
echo "Wrap-around Analysis:"
WRAP_COUNT=$(grep -c "wrap" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null || echo "0")
if [ $WRAP_COUNT -gt 0 ]; then
    echo "✓ Buffer wrap-around detected ($WRAP_COUNT occurrences)"
    grep -i "wrap" "$SCRIPT_DIR/reader_output.txt" | head -3
else
    echo "✗ No wrap-around detected (frames may be too small)"
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

exit $EXIT_CODE