#!/bin/bash

# Test JSON-RPC communication with serve process
echo "Testing C# serve process JSON-RPC communication..."

# Create a test request
cat <<EOF | /mnt/d/source/modelingevolution/streamer/src/zerobuffer/csharp/ZeroBuffer.Serve/bin/Debug/net9.0/ZeroBuffer.Serve 2>&1
Content-Length: 46

{"jsonrpc":"2.0","method":"health","id":1,"params":{}}
EOF

echo "Test completed"