#!/bin/bash

echo "Testing lock-free ZeroBuffer implementation..."

# Start reader in background with specific buffer config
./ZeroBuffer.Benchmarks/bin/Release/net9.0/ZeroBuffer.TestHelper reader test-lockfree 1024 65536 &
READER_PID=$!

# Give reader time to create the buffer
sleep 2

# Start writer
./ZeroBuffer.Benchmarks/bin/Release/net9.0/ZeroBuffer.TestHelper writer test-lockfree &
WRITER_PID=$!

# Let them communicate
sleep 3

# Send exit signal by killing writer
kill $WRITER_PID 2>/dev/null || true

# Wait a bit for reader to process remaining frames
sleep 1

# Kill reader
kill $READER_PID 2>/dev/null || true

echo "Test completed"