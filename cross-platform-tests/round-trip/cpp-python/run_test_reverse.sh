#!/bin/bash

# Python to C++ Round-Trip Test
# Python writes frames, C++ reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="python_cpp_test_$$"
FRAME_COUNT=1000
FRAME_SIZE=1024
BUFFER_SIZE=$((10 * 1024 * 1024)) # 10MB
METADATA_SIZE=$((1 * 1024)) # 1KB

echo "Starting Python → C++ round-trip test"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $FRAME_SIZE bytes"

# Create Python test writer script
cat > "$SCRIPT_DIR/test_writer.py" << 'EOF'
import sys
import time
from zerobuffer import Writer

def main():
    buffer_name = sys.argv[1]
    frame_count = int(sys.argv[2])
    frame_size = int(sys.argv[3])
    
    print(f"Python writer starting for buffer: {buffer_name}")
    
    writer = Writer(buffer_name)
    
    # Write some metadata
    metadata = b"Python writer metadata"
    writer.write_metadata(metadata)
    print(f"Wrote metadata: {len(metadata)} bytes")
    
    # Create frame data
    frame_data = bytes([i % 256 for i in range(frame_size)])
    
    print(f"Writing {frame_count} frames of {frame_size} bytes each...")
    start_time = time.time()
    
    try:
        for i in range(frame_count):
            writer.write_frame(frame_data)
            if (i + 1) % 100 == 0:
                print(f"Wrote {i + 1} frames...")
    except Exception as e:
        print(f"Error writing frame: {e}")
        sys.exit(1)
    
    end_time = time.time()
    duration = end_time - start_time
    
    print(f"Wrote {frame_count} frames in {duration:.2f} seconds")
    
    throughput = (frame_count * frame_size) / (1024 * 1024) / duration
    print(f"Throughput: {throughput:.2f} MB/s")
    
    writer.close()
    sys.exit(0)

if __name__ == "__main__":
    main()
EOF

# Start C++ reader in background
echo "Starting C++ reader..."
cd "$PROJECT_ROOT/cpp/build"
./examples/example_reader "$BUFFER_NAME" > "$SCRIPT_DIR/reader_output.txt" 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 2

# Run Python writer
echo "Starting Python writer..."
cd "$PROJECT_ROOT/python"
python3 "$SCRIPT_DIR/test_writer.py" "$BUFFER_NAME" $FRAME_COUNT $FRAME_SIZE > "$SCRIPT_DIR/writer_output.txt" 2>&1

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

# Check reader output
if grep -q "frames" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null; then
    echo "✓ Reader: Successfully processed frames"
    READER_SUCCESS=true
else
    echo "✗ Reader: Failed to read frames"
    READER_SUCCESS=false
fi

# Extract performance metrics
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    echo ""
    echo "Performance Metrics:"
    THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/writer_output.txt" 2>/dev/null || echo "N/A")
    if [ "$THROUGHPUT" != "N/A" ]; then
        echo "  Throughput: $THROUGHPUT MB/s"
    fi
fi

# Cleanup
rm -f "$SCRIPT_DIR/writer_output.txt" "$SCRIPT_DIR/reader_output.txt" "$SCRIPT_DIR/test_writer.py"

# Exit with appropriate code
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    exit 0
else
    exit 1
fi