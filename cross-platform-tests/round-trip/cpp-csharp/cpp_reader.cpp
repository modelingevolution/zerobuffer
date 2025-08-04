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
