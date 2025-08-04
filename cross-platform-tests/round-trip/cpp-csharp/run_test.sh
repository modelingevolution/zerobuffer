#!/bin/bash

# C++ to C# Round-Trip Test
# C++ writes frames, C# reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="cpp_csharp_test_$$"
FRAME_COUNT=1000
FRAME_SIZE=1024
BUFFER_SIZE=$((10 * 1024 * 1024)) # 10MB

echo "Starting C++ → C# round-trip test"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $FRAME_SIZE bytes"

# Start C# reader in background
echo "Starting C# reader..."
cd "$PROJECT_ROOT/csharp"
dotnet run --project ZeroBuffer.TestHelper -- reader "$BUFFER_NAME" \
    --frame-count $FRAME_COUNT \
    --validate \
    --measure > "$SCRIPT_DIR/reader_output.txt" 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 2

# Run C++ writer
echo "Starting C++ writer..."
cd "$PROJECT_ROOT/cpp/build"
./examples/example_writer "$BUFFER_NAME" $FRAME_COUNT $FRAME_SIZE > "$SCRIPT_DIR/writer_output.txt" 2>&1

# Wait for reader to complete
echo "Waiting for reader to finish..."
if wait $READER_PID; then
    echo "Reader completed successfully"
    READER_SUCCESS=true
else
    echo "Reader failed"
    READER_SUCCESS=false
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

# Check reader output
if grep -q "Read $FRAME_COUNT frames" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null; then
    echo "✓ Reader: Successfully read $FRAME_COUNT frames"
else
    echo "✗ Reader: Failed to read all frames"
    READER_SUCCESS=false
fi

# Extract performance metrics if available
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    echo ""
    echo "Performance Metrics:"
    # Extract throughput from C# reader output
    THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null || echo "N/A")
    if [ "$THROUGHPUT" != "N/A" ]; then
        echo "  Throughput: $THROUGHPUT MB/s"
    fi
    
    # Extract latency if available
    AVG_LATENCY=$(grep -oP "Average latency: \K[0-9.]+" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null || echo "N/A")
    if [ "$AVG_LATENCY" != "N/A" ]; then
        echo "  Average latency: $AVG_LATENCY μs"
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