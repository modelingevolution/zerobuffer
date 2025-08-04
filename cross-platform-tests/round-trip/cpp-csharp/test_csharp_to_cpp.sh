#!/bin/bash

# C# to C++ Round-Trip Test
# Creates a simple test without the complexities of the cross-platform test programs

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_NAME="csharp_cpp_simple_$$"
FRAME_COUNT=100
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420 Full HD
TARGET_FPS=30
DELAY_MS=$((1000 / TARGET_FPS))

echo "======================================"
echo "C# → C++ Simple Test"
echo "======================================"
echo "Buffer: $BUFFER_NAME"
echo "Frames: $FRAME_COUNT at ~$TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true
rm -f "$SCRIPT_DIR"/*.log 2>/dev/null || true

# Create a simple C++ reader that waits for writer
cat > "$SCRIPT_DIR/simple_reader.cpp" << 'EOF'
#include <zerobuffer/zerobuffer.h>
#include <zerobuffer/logger.h>
#include <iostream>
#include <chrono>
#include <thread>

using namespace zerobuffer;

int main(int argc, char* argv[]) {
    if (argc != 3) {
        std::cerr << "Usage: " << argv[0] << " <buffer-name> <frame-count>\n";
        return 1;
    }
    
    // Initialize logging
    init_logging(zerobuffer::debug);
    
    std::string buffer_name = argv[1];
    int frame_count = std::atoi(argv[2]);
    
    std::cout << "[READER] Creating buffer: " << buffer_name << std::endl;
    
    // Create buffer
    BufferConfig config(4096, 256 * 1024 * 1024); // 256MB
    Reader reader(buffer_name, config);
    
    std::cout << "[READER] Buffer created, waiting for writer..." << std::endl;
    
    // Wait for writer to connect (up to 10 seconds)
    for (int i = 0; i < 100; ++i) {
        if (reader.is_writer_connected()) {
            std::cout << "[READER] Writer connected!" << std::endl;
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    
    // Read frames
    int frames_read = 0;
    int errors = 0;
    auto start = std::chrono::high_resolution_clock::now();
    
    while (frames_read < frame_count) {
        try {
            Frame frame = reader.read_frame();
            if (!frame.valid()) {
                std::cout << "[READER] Invalid frame after " << frames_read << " frames" << std::endl;
                break;
            }
            
            frames_read++;
            
            // Verify sequential pattern (first byte should be frame index % 256)
            if (frame.size() > 0) {
                uint8_t expected = frames_read % 256;
                uint8_t actual = frame.data()[0];
                if (actual != expected) {
                    errors++;
                    if (errors <= 5) {
                        std::cout << "[READER] Frame " << frames_read 
                                  << ": Expected " << (int)expected 
                                  << ", got " << (int)actual << std::endl;
                    }
                }
            }
            
            reader.release_frame(frame);
            
            if (frames_read % 10 == 0) {
                std::cout << "[READER] Read " << frames_read << " frames..." << std::endl;
            }
        } catch (const WriterDeadException&) {
            std::cout << "[READER] Writer disconnected after " << frames_read << " frames" << std::endl;
            break;
        } catch (const std::exception& e) {
            std::cout << "[READER] Error: " << e.what() << std::endl;
            break;
        }
    }
    
    auto end = std::chrono::high_resolution_clock::now();
    auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count() / 1000.0;
    
    std::cout << "[READER] Completed: read " << frames_read << " frames in " << duration << " seconds" << std::endl;
    std::cout << "[READER] Errors: " << errors << std::endl;
    
    return (frames_read == frame_count && errors == 0) ? 0 : 1;
}
EOF

# Compile the simple reader
echo "Compiling simple C++ reader..."
cd "$PROJECT_ROOT/cpp/build"
g++ -std=c++17 -I../include -I/usr/include \
    "$SCRIPT_DIR/simple_reader.cpp" \
    -L. -lzerobuffer -lboost_log -lboost_thread -lboost_filesystem -lboost_system -lpthread -lrt \
    -o "$SCRIPT_DIR/simple_reader"

# Start C++ reader
echo ""
echo "Starting C++ reader..."
"$SCRIPT_DIR/simple_reader" "$BUFFER_NAME" "$FRAME_COUNT" 2>&1 | tee "$SCRIPT_DIR/reader.log" &
READER_PID=$!

# Wait for buffer creation
echo "Waiting for buffer creation..."
sleep 2

# Check if reader is still running
if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader died during startup"
    cat "$SCRIPT_DIR/reader.log"
    exit 1
fi

# Start C# writer
echo ""
echo "Starting C# writer..."
cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Release/net9.0" 2>/dev/null || \
    cd "$PROJECT_ROOT/csharp/ZeroBuffer.CrossPlatform/bin/Debug/net9.0"

./ZeroBuffer.CrossPlatform writer "$BUFFER_NAME" \
    --frames $FRAME_COUNT \
    --size $FRAME_SIZE \
    --pattern sequential \
    --delay-ms $DELAY_MS \
    --verbose \
    2>&1 | tee "$SCRIPT_DIR/writer.log"

WRITER_EXIT=$?

# Wait for reader to finish
echo ""
echo "Waiting for reader to finish..."
wait $READER_PID
READER_EXIT=$?

# Parse results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"

WRITER_FRAMES=$(grep -oP "wrote \K\d+" "$SCRIPT_DIR/writer.log" | tail -1 || echo "0")
READER_FRAMES=$(grep -oP "read \K\d+" "$SCRIPT_DIR/reader.log" | tail -1 || echo "0")
READER_ERRORS=$(grep -oP "Errors: \K\d+" "$SCRIPT_DIR/reader.log" || echo "N/A")

echo "Writer exit code: $WRITER_EXIT"
echo "Reader exit code: $READER_EXIT"
echo "Writer wrote: $WRITER_FRAMES frames"
echo "Reader read: $READER_FRAMES frames"
echo "Reader errors: $READER_ERRORS"

if [ "$WRITER_EXIT" -eq 0 ] && [ "$READER_EXIT" -eq 0 ] && [ "$WRITER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_FRAMES" = "$FRAME_COUNT" ]; then
    echo ""
    echo "✓ TEST PASSED"
    EXIT_CODE=0
else
    echo ""
    echo "✗ TEST FAILED"
    EXIT_CODE=1
fi

# Cleanup
rm -f "$SCRIPT_DIR/simple_reader" "$SCRIPT_DIR/simple_reader.cpp"
rm -f /dev/shm/*${BUFFER_NAME}* 2>/dev/null || true

exit $EXIT_CODE