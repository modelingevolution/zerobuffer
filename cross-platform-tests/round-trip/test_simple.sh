#!/bin/bash

# Simple C++ writer -> C# reader test
# Tests basic communication without complex error handling

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../.."
BUFFER_NAME="simple_test_$$"
FRAME_COUNT=10
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420 Full HD frame

echo "======================================"
echo "Simple C++ → C# Test"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT of size $((FRAME_SIZE / 1024 / 1024))MB each"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true

# Start C# reader
echo "Starting C# reader..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

# Create a simple reader that ignores WriterDeadException
cat > reader_test.cs << 'EOF'
using System;
using ZeroBuffer;

var bufferName = args[0];
var frameCount = int.Parse(args[1]);

var config = new BufferConfig(4096, 256 * 1024 * 1024);
using var reader = new Reader(bufferName, config);

Console.WriteLine($"Reader: Created buffer {bufferName}");
var framesRead = 0;

// Read all available frames
while (framesRead < frameCount)
{
    try 
    {
        var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
        if (!frame.IsValid) break;
        
        framesRead++;
        Console.WriteLine($"Reader: Read frame {framesRead}, size={frame.Size}");
    }
    catch (WriterDeadException)
    {
        // Continue reading remaining frames
        Console.WriteLine("Reader: Writer disconnected, checking for remaining frames...");
        
        // Try to read any remaining frames
        while (true)
        {
            try
            {
                var frame = reader.ReadFrame(TimeSpan.FromMilliseconds(100));
                if (!frame.IsValid) break;
                
                framesRead++;
                Console.WriteLine($"Reader: Read frame {framesRead}, size={frame.Size} (after writer died)");
            }
            catch
            {
                break;
            }
        }
        break;
    }
}

Console.WriteLine($"Reader: Total frames read: {framesRead}");
Environment.Exit(framesRead == frameCount ? 0 : 1);
EOF

# Compile and run the simple reader
dotnet run --project "$PROJECT_ROOT/csharp/ZeroBuffer/ZeroBuffer.csproj" -c Release > /dev/null 2>&1 || true
dotnet script reader_test.cs -- "$BUFFER_NAME" "$FRAME_COUNT" 2>&1 | tee reader.log &
READER_PID=$!

# Wait for buffer creation
echo "Waiting for buffer creation..."
sleep 3

# Run C++ writer
echo ""
echo "Starting C++ writer..."
cd "$PROJECT_ROOT/cpp/build"

./tests/zerobuffer-test-writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms 100 \
    2>&1 | tee writer.log

echo ""
echo "Waiting for reader to finish..."
wait $READER_PID
READER_EXIT=$?

echo ""
echo "======================================"
echo "Results:"
echo "======================================"

WRITER_FRAMES=$(grep -oP "wrote \K\d+" writer.log | tail -1 || echo "0")
READER_FRAMES=$(grep -oP "Total frames read: \K\d+" reader.log || echo "0")

echo "Writer wrote: $WRITER_FRAMES frames"
echo "Reader read: $READER_FRAMES frames"

if [ "$WRITER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_FRAMES" = "$FRAME_COUNT" ]; then
    echo "✓ TEST PASSED"
    EXIT_CODE=0
else
    echo "✗ TEST FAILED"
    EXIT_CODE=1
fi

# Cleanup
rm -f reader_test.cs reader.log writer.log
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true

exit $EXIT_CODE