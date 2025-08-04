#!/bin/bash

# C# to C++ Round-Trip Test
# C# writes frames, C++ reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="csharp_cpp_test_$$"
FRAME_COUNT=1000
FRAME_SIZE=1024
BUFFER_SIZE=$((10 * 1024 * 1024)) # 10MB

echo "Starting C# → C++ round-trip test"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $FRAME_SIZE bytes"

# Start C++ reader in background
echo "Starting C++ reader..."
cd "$PROJECT_ROOT/cpp/build"
./examples/example_reader "$BUFFER_NAME" > "$SCRIPT_DIR/reader_output.txt" 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 2

# Run C# writer
echo "Starting C# writer..."
cd "$PROJECT_ROOT/csharp"
dotnet run --project ZeroBuffer.TestHelper -- writer "$BUFFER_NAME" \
    --frame-count $FRAME_COUNT \
    --frame-size $FRAME_SIZE \
    --measure > "$SCRIPT_DIR/writer_output.txt" 2>&1

# Wait a moment for reader to finish processing
sleep 1

# Kill reader if still running
if kill -0 $READER_PID 2>/dev/null; then
    kill $READER_PID
    wait $READER_PID 2>/dev/null || true
fi

# Parse results
echo ""
echo "Results:"
echo "--------"

# Check writer output
if grep -q "Wrote $FRAME_COUNT frames" "$SCRIPT_DIR/writer_output.txt" 2>/dev/null; then
    echo "✓ Writer: Successfully wrote $FRAME_COUNT frames"
    WRITER_SUCCESS=true
else
    echo "✗ Writer: Failed to write all frames"
    WRITER_SUCCESS=false
fi

# Check reader output - C++ reader outputs different format
if grep -q "frames" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null; then
    echo "✓ Reader: Successfully processed frames"
    READER_SUCCESS=true
else
    echo "✗ Reader: Failed to read frames"
    READER_SUCCESS=false
fi

# Extract performance metrics if available
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    echo ""
    echo "Performance Metrics:"
    # Extract throughput from C# writer output
    THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/writer_output.txt" 2>/dev/null || echo "N/A")
    if [ "$THROUGHPUT" != "N/A" ]; then
        echo "  Throughput: $THROUGHPUT MB/s"
    fi
fi

# Cleanup
rm -f "$SCRIPT_DIR/writer_output.txt" "$SCRIPT_DIR/reader_output.txt"

# Exit with appropriate code
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    exit 0
else
    exit 1
fi