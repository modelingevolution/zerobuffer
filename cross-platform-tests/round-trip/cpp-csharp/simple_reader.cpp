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
