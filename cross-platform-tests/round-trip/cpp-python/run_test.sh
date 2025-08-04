#!/bin/bash

# C++ to Python Round-Trip Test
# C++ writes frames, Python reads and validates

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="cpp_python_test_$$"
FRAME_COUNT=1000
FRAME_SIZE=1024
BUFFER_SIZE=$((10 * 1024 * 1024)) # 10MB
METADATA_SIZE=$((1 * 1024)) # 1KB

echo "Starting C++ → Python round-trip test"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $FRAME_SIZE bytes"

# Create Python test reader script
cat > "$SCRIPT_DIR/test_reader.py" << 'EOF'
import sys
import time
from zerobuffer import Reader, BufferConfig

def main():
    buffer_name = sys.argv[1]
    expected_frames = int(sys.argv[2])
    
    print(f"Python reader starting for buffer: {buffer_name}")
    
    config = BufferConfig(metadata_size=1024, payload_size=10*1024*1024)
    reader = Reader(buffer_name, config)
    
    print(f"Buffer created, waiting for frames...")
    
    frames_read = 0
    start_time = time.time()
    
    try:
        while frames_read < expected_frames:
            frame = reader.read_frame(timeout=5.0)
            if frame:
                frames_read += 1
                if frames_read % 100 == 0:
                    print(f"Read {frames_read} frames...")
    except Exception as e:
        print(f"Error reading frame: {e}")
    
    end_time = time.time()
    duration = end_time - start_time
    
    print(f"Read {frames_read} frames in {duration:.2f} seconds")
    
    if frames_read > 0:
        throughput = (frames_read * 1024) / (1024 * 1024) / duration
        print(f"Throughput: {throughput:.2f} MB/s")
    
    reader.close()
    
    # Exit with success if we read all expected frames
    sys.exit(0 if frames_read == expected_frames else 1)

if __name__ == "__main__":
    main()
EOF

# Start Python reader in background
echo "Starting Python reader..."
cd "$PROJECT_ROOT/python"
python3 "$SCRIPT_DIR/test_reader.py" "$BUFFER_NAME" $FRAME_COUNT > "$SCRIPT_DIR/reader_output.txt" 2>&1 &
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

# Extract performance metrics
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    echo ""
    echo "Performance Metrics:"
    THROUGHPUT=$(grep -oP "Throughput: \K[0-9.]+" "$SCRIPT_DIR/reader_output.txt" 2>/dev/null || echo "N/A")
    if [ "$THROUGHPUT" != "N/A" ]; then
        echo "  Throughput: $THROUGHPUT MB/s"
    fi
fi

# Cleanup
rm -f "$SCRIPT_DIR/writer_output.txt" "$SCRIPT_DIR/reader_output.txt" "$SCRIPT_DIR/test_reader.py"

# Exit with appropriate code
if [ "$WRITER_SUCCESS" = true ] && [ "$READER_SUCCESS" = true ]; then
    exit 0
else
    exit 1
fi