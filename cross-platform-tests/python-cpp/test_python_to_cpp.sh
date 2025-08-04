#!/bin/bash
# Test Python writer -> C++ reader

set -e
echo "=== Python Writer -> C++ Reader Test ==="

# Configuration
BUFFER_NAME="test-py-cpp-$(date +%s)"
FRAME_SIZE=3225600  # 1920x1080 YUV420
NUM_FRAMES=100
FPS=30

# Paths
PYTHON_DIR="../../python"
CPP_DIR="../../cpp/build"

# Check binaries exist
if [ ! -f "$CPP_DIR/tests/zerobuffer-test-reader" ]; then
    echo "Error: C++ reader not found. Build C++ first."
    exit 1
fi

if [ ! -f "$PYTHON_DIR/venv/bin/python" ]; then
    echo "Error: Python venv not found. Run setup in python directory."
    exit 1
fi

# Start C++ reader
echo "Starting C++ reader..."
"$CPP_DIR/tests/zerobuffer-test-reader" "$BUFFER_NAME" &
READER_PID=$!

# Give reader time to create buffer
sleep 1

# Start Python writer
echo "Starting Python writer..."
cd "$PYTHON_DIR"
./venv/bin/python test_writer.py "$BUFFER_NAME" \
    --frame-size $FRAME_SIZE \
    --num-frames $NUM_FRAMES \
    --fps $FPS \
    --pattern sequential \
    --exit-frame &
WRITER_PID=$!

# Wait for writer to complete
wait $WRITER_PID
WRITER_EXIT=$?

# Wait for reader to complete
wait $READER_PID
READER_EXIT=$?

# Check results
if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo "✓ Test passed: Python -> C++ communication successful"
    exit 0
else
    echo "✗ Test failed: Writer exit=$WRITER_EXIT, Reader exit=$READER_EXIT"
    exit 1
fi