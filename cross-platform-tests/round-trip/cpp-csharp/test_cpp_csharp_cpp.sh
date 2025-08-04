#!/bin/bash

# Full C++ → C# → C++ Round-Trip Test
# Writer: C++, Relay: C#, Reader: C++

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/../../.."
BUFFER_A="cpp_to_csharp_$$"
BUFFER_B="csharp_to_cpp_$$"
FRAME_COUNT=300
FRAME_SIZE=$((1920 * 1080 * 3 / 2))  # YUV420 Full HD
TARGET_FPS=30
DELAY_MS=$((1000 / TARGET_FPS))

echo "======================================"
echo "C++ → C# → C++ Full Round-Trip Test"
echo "======================================"
echo "Buffer A (C++ → C#): $BUFFER_A"
echo "Buffer B (C# → C++): $BUFFER_B"
echo "Frames: $FRAME_COUNT at ~$TARGET_FPS FPS"
echo "Frame size: $((FRAME_SIZE / 1024 / 1024))MB"
echo ""

# Clean up
rm -f /dev/shm/*${BUFFER_A}* 2>/dev/null || true
rm -f /dev/shm/*${BUFFER_B}* 2>/dev/null || true
rm -f "$SCRIPT_DIR"/*.log 2>/dev/null || true

# Create a C++ writer with boost logging
cat > "$SCRIPT_DIR/cpp_writer.cpp" << 'EOF'
#include <zerobuffer/zerobuffer.h>
#include <zerobuffer/logger.h>
#include <iostream>
#include <chrono>
#include <thread>
#include <vector>

using namespace zerobuffer;

int main(int argc, char* argv[]) {
    // Initialize logging
    zerobuffer::init_logging(zerobuffer::debug);
    
    if (argc != 4) {
        std::cerr << "Usage: " << argv[0] << " <buffer-name> <frame-count> <frame-size>\n";
        return 1;
    }
    
    std::string buffer_name = argv[1];
    int frame_count = std::atoi(argv[2]);
    size_t frame_size = std::stoul(argv[3]);
    
    std::cout << "[WRITER] Creating buffer: " << buffer_name << std::endl;
    
    // Create reader first (it owns the buffer)
    BufferConfig config(4096, 256 * 1024 * 1024); // 256MB
    Reader reader(buffer_name, config);
    
    // Wait a moment then connect as writer
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    Writer writer(buffer_name);
    
    std::cout << "[WRITER] Buffer created, starting to write frames..." << std::endl;
    
    // Prepare frame data
    std::vector<uint8_t> frame_data(frame_size);
    
    // Calculate frame interval for 30 FPS
    auto frame_interval = std::chrono::milliseconds(33); // ~30 FPS
    auto next_frame = std::chrono::high_resolution_clock::now();
    
    auto start = std::chrono::high_resolution_clock::now();
    
    for (int i = 0; i < frame_count; ++i) {
        // Fill frame with sequential pattern
        for (size_t j = 0; j < frame_size; ++j) {
            frame_data[j] = (i + j) % 256;
        }
        
        // Write frame
        writer.write_frame(frame_data.data(), frame_size);
        
        if ((i + 1) % 10 == 0) {
            std::cout << "[WRITER] Wrote " << (i + 1) << " frames..." << std::endl;
        }
        
        // Pace the writes
        next_frame += frame_interval;
        std::this_thread::sleep_until(next_frame);
    }
    
    auto end = std::chrono::high_resolution_clock::now();
    auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count() / 1000.0;
    
    std::cout << "[WRITER] Completed: wrote " << frame_count << " frames in " << duration << " seconds" << std::endl;
    double throughput = (frame_count * frame_size / 1024.0 / 1024.0) / duration;
    std::cout << "[WRITER] Throughput: " << throughput << " MB/s" << std::endl;
    
    // Keep writer alive for a bit to ensure reader finishes
    std::this_thread::sleep_for(std::chrono::seconds(2));
    
    return 0;
}
EOF

# Create a C++ reader with boost logging
cat > "$SCRIPT_DIR/cpp_reader.cpp" << 'EOF'
#include <zerobuffer/zerobuffer.h>
#include <zerobuffer/logger.h>
#include <iostream>
#include <chrono>
#include <thread>

using namespace zerobuffer;

int main(int argc, char* argv[]) {
    // Initialize logging
    zerobuffer::init_logging(zerobuffer::debug);
    
    if (argc != 3) {
        std::cerr << "Usage: " << argv[0] << " <buffer-name> <frame-count>\n";
        return 1;
    }
    
    std::string buffer_name = argv[1];
    int frame_count = std::atoi(argv[2]);
    size_t frame_size = 0;  // Will be determined from actual frames
    
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
            
            // Get frame size from first frame
            if (frame_size == 0) {
                frame_size = frame.size();
            }
            
            // Verify sequential pattern
            if (frame.size() > 0) {
                const uint8_t* data = static_cast<const uint8_t*>(frame.data());
                bool frame_valid = true;
                
                // Check first few bytes
                for (int i = 0; i < std::min(100, (int)frame.size()); i++) {
                    uint8_t expected = ((frames_read - 1) + i) % 256;
                    if (data[i] != expected) {
                        frame_valid = false;
                        if (errors == 0) {
                            std::cout << "[READER] Frame " << frames_read 
                                      << " byte " << i 
                                      << ": Expected " << (int)expected 
                                      << ", got " << (int)data[i] << std::endl;
                        }
                        break;
                    }
                }
                
                if (!frame_valid) {
                    errors++;
                    if (errors <= 5) {
                        std::cout << "[READER] Frame " << frames_read << " failed verification" << std::endl;
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
    double throughput = (frames_read * frame_size / 1024.0 / 1024.0) / duration;
    std::cout << "[READER] Throughput: " << throughput << " MB/s" << std::endl;
    std::cout << "[READER] Verification errors: " << errors << std::endl;
    
    return (frames_read == frame_count && errors == 0) ? 0 : 1;
}
EOF

# Compile the C++ programs
echo "Compiling C++ writer and reader..."
cd "$PROJECT_ROOT/cpp/build"

g++ -std=c++17 -I../include -I/usr/include \
    -DBOOST_LOG_DYN_LINK \
    "$SCRIPT_DIR/cpp_writer.cpp" \
    -L. -lzerobuffer -lpthread -lrt \
    -lboost_log -lboost_log_setup -lboost_thread -lboost_filesystem -lboost_system \
    -o "$SCRIPT_DIR/cpp_writer"

g++ -std=c++17 -I../include -I/usr/include \
    -DBOOST_LOG_DYN_LINK \
    "$SCRIPT_DIR/cpp_reader.cpp" \
    -L. -lzerobuffer -lpthread -lrt \
    -lboost_log -lboost_log_setup -lboost_thread -lboost_filesystem -lboost_system \
    -o "$SCRIPT_DIR/cpp_reader"

# Start C# relay
echo ""
echo "Starting C# relay..."
cd "$PROJECT_ROOT/csharp"
dotnet run --project ZeroBuffer.CrossPlatform -- relay "$BUFFER_A" "$BUFFER_B" \
    --verbose \
    --log-interval 10 \
    2>&1 | tee "$SCRIPT_DIR/relay.log" &
RELAY_PID=$!

# Wait for relay to start
echo "Waiting for relay to initialize..."
sleep 2

# Check if relay is still running
if ! kill -0 $RELAY_PID 2>/dev/null; then
    echo "ERROR: Relay died during startup"
    cat "$SCRIPT_DIR/relay.log"
    exit 1
fi

# Start C++ reader
echo ""
echo "Starting C++ reader..."
"$SCRIPT_DIR/cpp_reader" "$BUFFER_B" "$FRAME_COUNT" 2>&1 | tee "$SCRIPT_DIR/reader.log" &
READER_PID=$!

# Wait for reader to create buffer
sleep 2

# Check if reader is still running
if ! kill -0 $READER_PID 2>/dev/null; then
    echo "ERROR: Reader died during startup"
    cat "$SCRIPT_DIR/reader.log"
    exit 1
fi

# Start C++ writer
echo ""
echo "Starting C++ writer..."
"$SCRIPT_DIR/cpp_writer" "$BUFFER_A" "$FRAME_COUNT" "$FRAME_SIZE" 2>&1 | tee "$SCRIPT_DIR/writer.log"
WRITER_EXIT=$?

# Wait for reader and relay to finish
echo ""
echo "Waiting for reader and relay to finish..."
wait $READER_PID
READER_EXIT=$?

# Give relay a moment to finish
sleep 1
kill $RELAY_PID 2>/dev/null || true
wait $RELAY_PID
RELAY_EXIT=$?

# Parse results
echo ""
echo "======================================"
echo "Results:"
echo "======================================"

WRITER_FRAMES=$(grep -oP "wrote \K\d+" "$SCRIPT_DIR/writer.log" | tail -1 || echo "0")
READER_FRAMES=$(grep -oP "read \K\d+" "$SCRIPT_DIR/reader.log" | tail -1 || echo "0")
READER_ERRORS=$(grep -oP "Verification errors: \K\d+" "$SCRIPT_DIR/reader.log" || echo "N/A")
RELAY_IN=$(grep -oP "Frames read: \K\d+" "$SCRIPT_DIR/relay.log" | tail -1 || echo "0")
RELAY_OUT=$(grep -oP "Frames written: \K\d+" "$SCRIPT_DIR/relay.log" | tail -1 || echo "0")

echo "Writer exit code: $WRITER_EXIT"
echo "Relay exit code: $RELAY_EXIT"
echo "Reader exit code: $READER_EXIT"
echo ""
echo "Writer wrote: $WRITER_FRAMES frames"
echo "Relay read: $RELAY_IN frames"
echo "Relay wrote: $RELAY_OUT frames"
echo "Reader read: $READER_FRAMES frames"
echo "Reader errors: $READER_ERRORS"

# Check for wrap-around in logs
echo ""
echo "Wrap-around events:"
grep -c "wrap-around\|wrap marker" "$SCRIPT_DIR/writer.log" 2>/dev/null && echo "  Writer: $(grep -c "wrap-around\|wrap marker" "$SCRIPT_DIR/writer.log") wrap(s)" || true
grep -c "wrap-around\|wrap marker" "$SCRIPT_DIR/relay.log" 2>/dev/null && echo "  Relay: $(grep -c "wrap-around\|wrap marker" "$SCRIPT_DIR/relay.log") wrap(s)" || true
grep -c "wrap-around\|wrap marker" "$SCRIPT_DIR/reader.log" 2>/dev/null && echo "  Reader: $(grep -c "wrap-around\|wrap marker" "$SCRIPT_DIR/reader.log") wrap(s)" || true

if [ "$WRITER_EXIT" -eq 0 ] && [ "$READER_EXIT" -eq 0 ] && [ "$WRITER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_FRAMES" = "$FRAME_COUNT" ] && [ "$READER_ERRORS" = "0" ]; then
    echo ""
    echo "✓ TEST PASSED"
    EXIT_CODE=0
else
    echo ""
    echo "✗ TEST FAILED"
    EXIT_CODE=1
fi

# Cleanup
rm -f "$SCRIPT_DIR/cpp_writer" "$SCRIPT_DIR/cpp_writer.cpp"
rm -f "$SCRIPT_DIR/cpp_reader" "$SCRIPT_DIR/cpp_reader.cpp"
rm -f /dev/shm/*${BUFFER_A}* 2>/dev/null || true
rm -f /dev/shm/*${BUFFER_B}* 2>/dev/null || true

exit $EXIT_CODE