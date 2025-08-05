#!/bin/bash

echo "Testing C# serve process JSON-RPC..."

# Test health check
echo '{"jsonrpc":"2.0","method":"health","params":{},"id":1}' | \
    /mnt/d/source/modelingevolution/streamer/src/zerobuffer/csharp/ZeroBuffer.Serve/bin/Debug/net9.0/ZeroBuffer.Serve 2>&1 | \
    grep -E "(result|error|true)" | head -5

echo "Test completed"