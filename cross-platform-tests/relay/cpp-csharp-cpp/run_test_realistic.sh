#!/bin/bash

# C++ → C# → C++ relay test with realistic parameters
# Writer: C++, Relay: C#, Reader: C++

# Test parameters matching benchmarks
BUFFER_INPUT="cpp_to_relay_test"
BUFFER_OUTPUT="relay_to_cpp_test"
FRAME_COUNT=1000
# YUV420 Full HD frame size (matching benchmarks)
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes
PATTERN="sequential"

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Running C++ → C# → C++ relay test (REALISTIC)${NC}"
echo "Input buffer: $BUFFER_INPUT"
echo "Output buffer: $BUFFER_OUTPUT"
echo "Frames: $FRAME_COUNT"
echo "Frame size: $FRAME_SIZE bytes (YUV420 Full HD + header)"
echo "Pattern: $PATTERN"
echo "-----------------------------------"

# Clean up any existing buffers
rm -f /dev/shm/$BUFFER_INPUT* 2>/dev/null
rm -f /dev/shm/$BUFFER_OUTPUT* 2>/dev/null
rm -f /dev/shm/sem.sem-* 2>/dev/null

# Create results directory if it doesn't exist
mkdir -p ../../results

# Step 1: Start C# relay first (it creates the input buffer as Reader)
echo -e "${YELLOW}Starting C# relay...${NC}"
cd ../../../csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0
./ZeroBuffer.CrossPlatform relay "$BUFFER_INPUT" "$BUFFER_OUTPUT" \
    --frames 0 \
    --verbose \
    --log-interval 10 \
    --timeout-ms 10000 \
    --json-output \
    > ../../../../../cross-platform-tests/results/cpp_relay_realistic_csharp.json 2>../../../../../cross-platform-tests/results/cpp_relay_realistic_csharp.err &
RELAY_PID=$!

# Give relay time to create the input buffer
sleep 3

# Step 2: Start C++ reader (creates the output buffer)
echo -e "${YELLOW}Starting C++ reader...${NC}"
cd ../../../../../cpp/build
./tests/zerobuffer-test-reader "$BUFFER_OUTPUT" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify $PATTERN \
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/cpp_relay_realistic_reader.json 2>&1 &
READER_PID=$!

# Give reader time to create the output buffer
sleep 5

# Step 3: Start C++ writer (connects to the input buffer)
echo -e "${YELLOW}Starting C++ writer...${NC}"
# Add small delay between frames to avoid overwhelming the relay
./tests/zerobuffer-test-writer "$BUFFER_INPUT" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern $PATTERN \
    --delay-ms 10 \
    --verbose \
    --json-output \
    > ../../cross-platform-tests/results/cpp_relay_realistic_writer.json 2>&1 &
WRITER_PID=$!

# Wait for writer to complete
wait $WRITER_PID
WRITER_EXIT=$?

# Give relay and reader time to process remaining frames
echo -e "${YELLOW}Waiting for relay and reader to finish...${NC}"
sleep 30

# Check if processes are still running
if kill -0 $RELAY_PID 2>/dev/null; then
    echo "Relay still running, killing..."
    kill $RELAY_PID
fi

if kill -0 $READER_PID 2>/dev/null; then
    echo "Reader still running, killing..."
    kill $READER_PID
fi

wait $RELAY_PID 2>/dev/null
RELAY_EXIT=$?
wait $READER_PID 2>/dev/null
READER_EXIT=$?

# Check results
echo ""
echo "Results:"
echo "--------"
echo "Writer exit code: $WRITER_EXIT"
echo "Relay exit code: $RELAY_EXIT"
echo "Reader exit code: $READER_EXIT"

# Show summary from JSON outputs
echo ""
echo -e "${YELLOW}Performance Summary:${NC}"

if [ -f ../../cross-platform-tests/results/cpp_relay_realistic_writer.json ]; then
    WRITER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_writer.json'))['frames_written'])" 2>/dev/null || echo "?")
    WRITER_THROUGHPUT=$(python3 -c "import json; print(f\"{json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_writer.json'))['throughput_mbps']:.2f}\")" 2>/dev/null || echo "?")
    echo "Writer: $WRITER_FRAMES frames, $WRITER_THROUGHPUT MB/s"
fi

if [ -f ../../cross-platform-tests/results/cpp_relay_realistic_csharp.json ]; then
    RELAY_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_csharp.json')).get('FramesRelayed', '?'))" 2>/dev/null || echo "?")
    RELAY_THROUGHPUT=$(python3 -c "import json; print(f\"{json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_csharp.json')).get('ThroughputMbps', 0):.2f}\")" 2>/dev/null || echo "?")
    echo "Relay: $RELAY_FRAMES frames, $RELAY_THROUGHPUT MB/s"
fi

if [ -f ../../cross-platform-tests/results/cpp_relay_realistic_reader.json ]; then
    READER_FRAMES=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_reader.json'))['frames_read'])" 2>/dev/null || echo "?")
    READER_THROUGHPUT=$(python3 -c "import json; print(f\"{json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_reader.json'))['throughput_mbps']:.2f}\")" 2>/dev/null || echo "?")
    READER_ERRORS=$(python3 -c "import json; print(json.load(open('../../cross-platform-tests/results/cpp_relay_realistic_reader.json'))['verification_errors'])" 2>/dev/null || echo "?")
    echo "Reader: $READER_FRAMES frames, $READER_THROUGHPUT MB/s, $READER_ERRORS errors"
fi

# Determine overall success
if [ $WRITER_EXIT -eq 0 ] && [ "$WRITER_FRAMES" = "$READER_FRAMES" ] && [ "$READER_ERRORS" = "0" ]; then
    echo ""
    echo -e "${GREEN}Test PASSED - All frames relayed successfully${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}Test FAILED${NC}"
    exit 1
fi