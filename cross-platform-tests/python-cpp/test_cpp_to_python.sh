#!/bin/bash
# Test C++ writer -> Python reader

set -e
echo "=== C++ Writer -> Python Reader Test ==="

# Configuration
BUFFER_NAME="test-cpp-py-$(date +%s)"
FRAME_SIZE=3225600  # 1920x1080 YUV420
NUM_FRAMES=100
FPS=30

# Paths
PYTHON_DIR="../../python"
CPP_DIR="../../cpp/build"

# Check binaries exist
if [ ! -f "$CPP_DIR/tests/zerobuffer-test-writer" ]; then
    echo "Error: C++ writer not found. Build C++ first."
    exit 1
fi

if [ ! -f "$PYTHON_DIR/venv/bin/python" ]; then
    echo "Error: Python venv not found. Run setup in python directory."
    exit 1
fi

# Start Python reader
echo "Starting Python reader..."
cd "$PYTHON_DIR"
./venv/bin/python test_reader.py "$BUFFER_NAME" \
    --expected-frames $NUM_FRAMES \
    --verify \
    --exit-on-exit-frame &
READER_PID=$!
cd - > /dev/null

# Give reader time to create buffer
sleep 1

# Start C++ writer
echo "Starting C++ writer..."
"$CPP_DIR/tests/zerobuffer-test-writer" "$BUFFER_NAME" \
    --frames $NUM_FRAMES \
    --pattern sequential \
    --fps $FPS \
    --exit-frame &
WRITER_PID=$!

# Wait for both to complete
wait $WRITER_PID
WRITER_EXIT=$?

wait $READER_PID
READER_EXIT=$?

# Check results
if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo "✓ Test passed: C++ -> Python communication successful"
    exit 0
else
    echo "✗ Test failed: Writer exit=$WRITER_EXIT, Reader exit=$READER_EXIT"
    exit 1
fi