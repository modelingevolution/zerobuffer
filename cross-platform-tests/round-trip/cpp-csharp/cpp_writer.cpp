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
