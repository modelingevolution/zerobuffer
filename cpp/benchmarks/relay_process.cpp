#include <zerobuffer/zerobuffer.h>
#include <iostream>
#include <csignal>
#include <atomic>
#include <thread>
#include <chrono>
#include <cstring>

using namespace zerobuffer;

// Frame structure matching C# implementation
#pragma pack(push, 1)
struct TimestampedFrame {
    int64_t timestamp;    // 8 bytes - high resolution timestamp
    int32_t frame_id;     // 4 bytes
    int32_t padding;      // 4 bytes - align to 16 bytes
    // Followed by YUV420 data
};
#pragma pack(pop)

// Constants
constexpr size_t YUV420_FRAME_SIZE = 1920 * 1080 * 3 / 2;  // 3,110,400 bytes
constexpr size_t HEADER_SIZE = sizeof(TimestampedFrame);
constexpr size_t TOTAL_FRAME_SIZE = HEADER_SIZE + YUV420_FRAME_SIZE;
constexpr size_t BUFFER_SIZE = 256 * 1024 * 1024;  // 256 MB

std::atomic<bool> should_exit(false);

void signal_handler(int) {
    should_exit = true;
}

int main() {
    std::signal(SIGINT, signal_handler);
    std::signal(SIGTERM, signal_handler);
    
    std::cout << "Relay process starting..." << std::endl;
    
    try {
        // Create Buffer A as Reader (input buffer)
        std::cout << "Creating Buffer A (input)..." << std::endl;
        BufferConfig config(0, BUFFER_SIZE);
        Reader reader("buffer-a", config);
        
        // Wait for Buffer B to be created by benchmark process
        std::cout << "Waiting for Buffer B to be created..." << std::endl;
        Writer* writer = nullptr;
        
        while (!should_exit && !writer) {
            try {
                writer = new Writer("buffer-b");
                std::cout << "Connected to Buffer B (output)" << std::endl;
            } catch (const std::exception&) {
                // Buffer B not ready yet
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
        }
        
        if (!writer) {
            std::cout << "Exiting without connecting to Buffer B" << std::endl;
            return 1;
        }
        
        std::cout << "Relay ready - starting frame relay..." << std::endl;
        
        // Main relay loop
        size_t frames_relayed = 0;
        std::vector<uint8_t> frame_buffer(TOTAL_FRAME_SIZE);
        
        // Note: We don't validate sequence numbers in relay - just pass through
        while (!should_exit) {
            try {
                // Read frame from Buffer A
                Frame frame = reader.read_frame(std::chrono::seconds(5));
                
                if (!frame.valid()) {
                    continue;
                }
                
                // Verify frame size
                if (frame.size() != TOTAL_FRAME_SIZE) {
                    std::cerr << "Invalid frame size: " << frame.size() 
                              << " (expected " << TOTAL_FRAME_SIZE << ")" << std::endl;
                    reader.release_frame(frame);
                    continue;
                }
                
                // Use zero-copy write to relay the frame
                uint64_t sequence;
                void* write_buffer = writer->get_frame_buffer(TOTAL_FRAME_SIZE, sequence);
                
                // Copy frame data directly (this preserves the original timestamp)
                std::memcpy(write_buffer, frame.data(), TOTAL_FRAME_SIZE);
                
                // Commit the frame
                writer->commit_frame();
                
                // Release the read frame
                reader.release_frame(frame);
                
                frames_relayed++;
                
                if (frames_relayed % 10 == 0) {
                    std::cout << "[RELAY DEBUG] Relayed " << frames_relayed << " frames" << std::endl;
                }
                
            } catch (const WriterDeadException&) {
                std::cout << "Benchmark process disconnected" << std::endl;
                break;
            } catch (const SequenceError& e) {
                // This is expected when benchmark restarts - just exit cleanly
                std::cout << "Sequence reset detected, relay shutting down" << std::endl;
                break;
            } catch (const std::exception& e) {
                std::cerr << "Error in relay loop: " << e.what() << std::endl;
                break;
            }
        }
        
        std::cout << "Relay process shutting down. Total frames relayed: " 
                  << frames_relayed << std::endl;
        
        delete writer;
        
    } catch (const std::exception& e) {
        std::cerr << "Fatal error: " << e.what() << std::endl;
        return 1;
    }
    
    return 0;
}