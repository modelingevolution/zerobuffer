#!/bin/bash
# Test Python <-> C++ interoperability

set -e
echo "=== Python <-> C++ Interoperability Test ==="

# Run Python -> C++ test
echo "Testing Python writer -> C++ reader..."
BUFFER_NAME="test-py-cpp-$(date +%s)"

# Start C++ reader
../cpp/build/tests/zerobuffer-test-reader "$BUFFER_NAME" &
READER_PID=$!
sleep 1

# Start Python writer
./venv/bin/python test_writer.py "$BUFFER_NAME" \
    --frame-size 1024 \
    --num-frames 10 \
    --fps 10 \
    --pattern sequential \
    --exit-frame &
WRITER_PID=$!

# Wait for completion
wait $WRITER_PID
WRITER_EXIT=$?
wait $READER_PID
READER_EXIT=$?

if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo "✓ Python -> C++ test passed"
else
    echo "✗ Python -> C++ test failed"
    exit 1
fi

echo ""
echo "Testing C++ writer -> Python reader..."
BUFFER_NAME="test-cpp-py-$(date +%s)"

# Start Python reader
./venv/bin/python test_reader.py "$BUFFER_NAME" \
    --expected-frames 10 \
    --verify \
    --exit-on-exit-frame &
READER_PID=$!
sleep 1

# Start C++ writer
../cpp/build/tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames 10 \
    --pattern sequential \
    --fps 10 \
    --exit-frame &
WRITER_PID=$!

# Wait for completion
wait $WRITER_PID
WRITER_EXIT=$?
wait $READER_PID
READER_EXIT=$?

if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo "✓ C++ -> Python test passed"
else
    echo "✗ C++ -> Python test failed"
    exit 1
fi

echo ""
echo "✓ All Python <-> C++ tests passed"