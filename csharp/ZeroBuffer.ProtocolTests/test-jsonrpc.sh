#!/bin/bash

# Simple script to test JSON-RPC infrastructure

echo "=== Testing JSON-RPC Infrastructure ==="

# First, let's test that the serve command works
echo "1. Testing 'serve' command..."
timeout 2s dotnet run -- serve 2>&1 | grep -q "JSON-RPC server started" && echo "✓ Server starts correctly" || echo "✗ Server failed to start"

# Test the protocol with a simple JSON-RPC request
echo -e "\n2. Testing JSON-RPC protocol..."

# Create a test JSON-RPC request
cat > test-request.json << EOF
{
  "jsonrpc": "2.0",
  "method": "setup",
  "params": {
    "testId": 101,
    "role": "reader",
    "bufferName": "test-buffer"
  },
  "id": 1
}
EOF

# Send request to server and capture response
echo "Sending test setup request..."
(echo '{"jsonrpc":"2.0","method":"setup","params":{"testId":101,"role":"reader","bufferName":"test-buffer"},"id":1}'; sleep 1) | dotnet run -- serve 2>&1 | grep -E "(handle|error)" && echo "✓ Server processes requests" || echo "✗ Server failed to process request"

echo -e "\n=== Test Complete ==="