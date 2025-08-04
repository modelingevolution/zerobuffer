#!/bin/bash

# Test wrap-around behavior between C++ and C#
# This test focuses on the critical wrap-around boundary at frame 86/87

BUFFER_NAME="wrap_around_test"
FRAME_COUNT=90  # Just enough to test wrap-around (86 frames fit, 87th wraps)
HEADER_SIZE=16
YUV420_SIZE=$((1920 * 1080 * 3 / 2))
FRAME_SIZE=$((HEADER_SIZE + YUV420_SIZE))  # 3,110,416 bytes

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Testing Wrap-Around Behavior${NC}"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT (wrap at frame 87)"
echo "Frame size: $FRAME_SIZE bytes"
echo "Buffer holds: 86 complete frames"
echo "-----------------------------------"

# Clean up any existing buffers
rm -f /dev/shm/$BUFFER_NAME* 2>/dev/null
rm -f /dev/shm/sem.sem-* 2>/dev/null

# Create results directory
mkdir -p ../results

# Test 1: C++ Writer → C# Reader
echo ""
echo -e "${YELLOW}Test 1: C++ Writer → C# Reader${NC}"

# Start C# reader (creates buffer with 256MB)
cd ../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --create \
    --buffer-size $((256 * 1024 * 1024)) \
    --verify sequential \
    --verbose \
    > ../cross-platform-tests/results/wrap_test1_csharp_reader.log 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 3

# Start C++ writer
cd ../cpp/build
./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    > ../../cross-platform-tests/results/wrap_test1_cpp_writer.log 2>&1 &
WRITER_PID=$!

# Wait for both to complete
wait $WRITER_PID
WRITER_EXIT=$?

# Give reader time to finish
sleep 5

if kill -0 $READER_PID 2>/dev/null; then
    kill $READER_PID
fi
wait $READER_PID 2>/dev/null
READER_EXIT=$?

echo "Writer exit: $WRITER_EXIT, Reader exit: $READER_EXIT"

# Extract frame counts
WRITER_FRAMES=$(grep "frames_written" ../../cross-platform-tests/results/wrap_test1_cpp_writer.log | grep -oP '\d+' | head -1 || echo "0")
READER_FRAMES=$(grep "FramesRead" ../../cross-platform-tests/results/wrap_test1_csharp_reader.log | grep -oP '\d+' | head -1 || echo "0")

echo "Frames written: $WRITER_FRAMES, Frames read: $READER_FRAMES"

# Clean up
rm -f /dev/shm/$BUFFER_NAME* 2>/dev/null
sleep 2

# Test 2: C# Writer → C++ Reader  
echo ""
echo -e "${YELLOW}Test 2: C# Writer → C++ Reader${NC}"

# Start C++ reader (creates buffer with 256MB)
cd ../cpp/build
./tests/zerobuffer-test-reader "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --verify sequential \
    --verbose \
    > ../../cross-platform-tests/results/wrap_test2_cpp_reader.log 2>&1 &
READER_PID=$!

# Give reader time to create buffer
sleep 3

# Start C# writer
cd ../../csharp
dotnet run --project ZeroBuffer.CrossPlatform -- writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --verbose \
    > ../cross-platform-tests/results/wrap_test2_csharp_writer.log 2>&1 &
WRITER_PID=$!

# Wait for both to complete
wait $WRITER_PID
WRITER_EXIT=$?

# Give reader time to finish
sleep 5

if kill -0 $READER_PID 2>/dev/null; then
    kill $READER_PID
fi
wait $READER_PID 2>/dev/null
READER_EXIT=$?

echo "Writer exit: $WRITER_EXIT, Reader exit: $READER_EXIT"

# Extract frame counts
WRITER_FRAMES=$(grep "FramesWritten" ../../cross-platform-tests/results/wrap_test2_csharp_writer.log | grep -oP '\d+' | head -1 || echo "0")
READER_FRAMES=$(grep "frames_read" ../../cross-platform-tests/results/wrap_test2_cpp_reader.log | grep -oP '\d+' | head -1 || echo "0")

echo "Frames written: $WRITER_FRAMES, Frames read: $READER_FRAMES"

# Summary
echo ""
echo -e "${YELLOW}Summary:${NC}"
echo "Test 1 (C++ → C#): Both should handle 90 frames (with wrap-around)"
echo "Test 2 (C# → C++): Both should handle 90 frames (with wrap-around)"

# Show any errors
echo ""
echo -e "${YELLOW}Checking for errors around frame 86/87:${NC}"
grep -n "frame 8[567]" ../results/wrap_test*.log 2>/dev/null || echo "No errors found around wrap boundary"
grep -n -i "wrap\|invalid\|error" ../results/wrap_test*.log 2>/dev/null | grep -v "No errors" || echo "No wrap-related errors found"