#!/bin/bash

# C++ to C# Round-Trip Test with Debug
# C++ writes frames, C# reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="cpp_csharp_debug"
FRAME_COUNT=100  # Smaller for debugging
FRAME_SIZE=1024  # Just data size, header will be added
METADATA_SIZE=4096
BUFFER_SIZE=$((256 * 1024 * 1024)) # 256MB

echo "======================================"
echo "C++ → C# Round-Trip Test (DEBUG)"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $FRAME_SIZE bytes"
echo ""

# Clean up any previous resources
echo "Cleaning up previous resources..."
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true
rm -f /dev/shm/sem.*${BUFFER_NAME}* 2>/dev/null || true

# Start C# reader in background
echo "[1/3] Starting C# reader..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

export DOTNET_LOG_LEVEL=Debug
./ZeroBuffer.CrossPlatform reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --verify sequential \
    --metadata-size $METADATA_SIZE \
    --buffer-size $BUFFER_SIZE \
    --verbose \
    2>&1 | tee "$SCRIPT_DIR/reader_debug.log" &
READER_PID=$!

# Wait and check if buffer was created
echo "      Waiting for buffer creation..."
for i in {1..10}; do
    if [ -f /dev/shm/${BUFFER_NAME} ]; then
        echo "      Buffer created after $i seconds"
        break
    fi
    sleep 1
done

# Check if reader is still running
if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader died early"
    tail -20 "$SCRIPT_DIR/reader_debug.log"
    exit 1
fi

# Run C++ writer
echo "[2/3] Starting C++ writer..."
cd "$PROJECT_ROOT/cpp/build"

# Set debug logging for C++
export ZEROBUFFER_LOG_LEVEL=debug

./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    2>&1 | tee "$SCRIPT_DIR/writer_debug.log"

WRITER_EXIT=${PIPESTATUS[0]}

# Wait for reader
echo "[3/3] Waiting for reader..."
wait $READER_PID
READER_EXIT=$?

echo ""
echo "======================================"
echo "Results:"
echo "======================================"

# Check results
echo "Writer exit code: $WRITER_EXIT"
echo "Reader exit code: $READER_EXIT"

echo ""
echo "Writer output:"
tail -10 "$SCRIPT_DIR/writer_debug.log"

echo ""
echo "Reader output:"
tail -10 "$SCRIPT_DIR/reader_debug.log"

# Check for specific errors
echo ""
echo "Error analysis:"
grep -i "error\|exception\|fail" "$SCRIPT_DIR/reader_debug.log" | head -10 || echo "No errors in reader log"
grep -i "error\|exception\|fail" "$SCRIPT_DIR/writer_debug.log" | head -10 || echo "No errors in writer log"

# Cleanup
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true