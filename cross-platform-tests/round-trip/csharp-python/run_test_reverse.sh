#!/bin/bash

# Python → C# round-trip test
# Writer: Python, Reader: C#

# Test parameters
BUFFER_NAME="python_to_csharp_test"
FRAME_COUNT=1000
FRAME_SIZE=1024
PATTERN="sequential"

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo "Running Python → C# round-trip test"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes"
echo "Pattern: $PATTERN"
echo "-----------------------------------"

# Clean up any existing buffer
rm -f /dev/shm/$BUFFER_NAME* 2>/dev/null

# Start C# reader in background
echo "Starting C# reader..."
cd ../../../csharp/ZeroBuffer.CrossPlatform
dotnet run -- reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify $PATTERN \
    --json-output \
    > ../../cross-platform-tests/results/python_csharp_reader.json &
READER_PID=$!

# Give reader time to start
sleep 2

# Start Python writer
echo "Starting Python writer..."
cd ../../python
python3 -m zerobuffer.cross_platform writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern $PATTERN \
    --json-output \
    > ../cross-platform-tests/results/python_csharp_writer.json

WRITER_EXIT=$?

# Wait for reader to complete
echo "Waiting for reader to complete..."
wait $READER_PID
READER_EXIT=$?

# Check results
echo ""
echo "Results:"
echo "--------"
echo "Writer exit code: $WRITER_EXIT"
echo "Reader exit code: $READER_EXIT"

if [ $WRITER_EXIT -eq 0 ] && [ $READER_EXIT -eq 0 ]; then
    echo -e "${GREEN}Test PASSED${NC}"
    
    # Show throughput if available
    if [ -f ../cross-platform-tests/results/python_csharp_writer.json ]; then
        WRITER_THROUGHPUT=$(python3 -c "import json; print(json.load(open('../cross-platform-tests/results/python_csharp_writer.json'))['throughput_mbps'])" 2>/dev/null)
        [ -n "$WRITER_THROUGHPUT" ] && echo "Writer throughput: ${WRITER_THROUGHPUT} MB/s"
    fi
    
    if [ -f ../cross-platform-tests/results/python_csharp_reader.json ]; then
        READER_THROUGHPUT=$(python3 -c "import json; print(json.load(open('../cross-platform-tests/results/python_csharp_reader.json'))['throughput_mbps'])" 2>/dev/null)
        READER_ERRORS=$(python3 -c "import json; r=json.load(open('../cross-platform-tests/results/python_csharp_reader.json')); print(r['verification_errors'])" 2>/dev/null)
        [ -n "$READER_THROUGHPUT" ] && echo "Reader throughput: ${READER_THROUGHPUT} MB/s"
        [ -n "$READER_ERRORS" ] && echo "Verification errors: $READER_ERRORS"
    fi
    
    exit 0
else
    echo -e "${RED}Test FAILED${NC}"
    
    # Show errors if any
    if [ -f ../cross-platform-tests/results/python_csharp_writer.json ]; then
        echo "Writer output:"
        cat ../cross-platform-tests/results/python_csharp_writer.json
    fi
    
    if [ -f ../cross-platform-tests/results/python_csharp_reader.json ]; then
        echo "Reader output:"
        cat ../cross-platform-tests/results/python_csharp_reader.json
    fi
    
    exit 1
fi