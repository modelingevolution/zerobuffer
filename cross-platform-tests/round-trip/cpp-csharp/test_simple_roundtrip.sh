#!/bin/bash

# Simple C++ → C# → C++ Round-Trip Test
# Uses the same approach as the working individual tests

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="roundtrip_$$"
FRAME_COUNT=100
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420 Full HD
TARGET_FPS=30
DELAY_MS=$((1000 / TARGET_FPS))

echo "======================================"
echo "Simple C++ → C# → C++ Round-Trip Test"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT at ~$TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true
rm -f "$SCRIPT_DIR"/*.log 2>/dev/null || true

# Step 1: Run C++ → C# test
echo "Phase 1: Testing C++ → C# ..."
echo "-----------------------------------"
./test_cpp_to_csharp.sh
if [ $? -ne 0 ]; then
    echo "C++ → C# test failed"
    exit 1
fi

echo ""
echo "Phase 2: Testing C# → C++ ..."
echo "-----------------------------------"
./test_csharp_to_cpp.sh
if [ $? -ne 0 ]; then
    echo "C# → C++ test failed"
    exit 1
fi

echo ""
echo "========================================"
echo "✓ Both round-trip tests passed!"
echo "========================================"