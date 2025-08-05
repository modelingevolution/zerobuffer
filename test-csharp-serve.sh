#!/bin/bash

echo "Testing C# serve process directly..."

# Start the C# serve process
/mnt/d/source/modelingevolution/streamer/src/zerobuffer/csharp/ZeroBuffer.Serve/bin/Debug/net9.0/ZeroBuffer.Serve &
SERVE_PID=$!

sleep 2

# Send a health check
echo '{"jsonrpc":"2.0","method":"health","params":{},"id":1}' | nc -q 1 localhost 5006

# Kill the serve process
kill $SERVE_PID 2>/dev/null

echo "Test completed"