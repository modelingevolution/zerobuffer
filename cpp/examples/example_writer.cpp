#include <zerobuffer/zerobuffer.h>
#include <iostream>
#include <chrono>
#include <thread>
#include <random>

int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::cerr << "Usage: " << argv[0] << " <buffer-name>\n";
        std::cerr << "Example: " << argv[0] << " my-buffer\n";
        return 1;
    }
    
    std::string buffer_name = argv[1];
    
    try {
        std::cout << "Connecting to ZeroBuffer: " << buffer_name << "\n";
        
        zerobuffer::Writer writer(buffer_name);
        
        std::cout << "Connected!\n";
        
        // Write metadata
        std::string meta_str = "Example metadata: frame format=RGB24, fps=30";
        writer.set_metadata(meta_str.data(), meta_str.size());
        std::cout << "Wrote metadata: " << meta_str << "\n";
        
        // Setup for frame generation
        std::random_device rd;
        std::mt19937 gen(rd());
        std::uniform_int_distribution<> size_dist(1000, 10000);
        
        std::cout << "\nWriting frames... (Press Ctrl+C to stop)\n";
        
        uint64_t total_frames = 0;
        auto start_time = std::chrono::steady_clock::now();
        
        while (true) {
            try {
                // Generate random frame size
                size_t frame_size = size_dist(gen);
                
                // Create frame data (could be actual video data)
                std::vector<uint8_t> frame_data(frame_size);
                // Fill with pattern for verification
                for (size_t i = 0; i < frame_size; ++i) {
                    frame_data[i] = (total_frames + i) % 256;
                }
                
                // Write frame
                writer.write_frame(frame_data);
                total_frames++;
                
                // Print stats every 100 frames
                if (total_frames % 100 == 0) {
                    auto now = std::chrono::steady_clock::now();
                    auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(now - start_time).count();
                    
                    std::cout << "Frames written: " << total_frames
                              << ", Rate: " << (elapsed > 0 ? total_frames / elapsed : 0) 
                              << " fps, Reader connected: " << (writer.is_reader_connected() ? "yes" : "no")
                              << "\n";
                }
                
                // Simulate frame rate (30 fps)
                std::this_thread::sleep_for(std::chrono::milliseconds(33));
                
            } catch (const zerobuffer::ReaderDeadException&) {
                std::cout << "\nReader disconnected. Exiting.\n";
                break;
            } catch (const zerobuffer::BufferFullException&) {
                std::cout << "\nBuffer full, reader not keeping up.\n";
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
        }
        
        std::cout << "\nTotal frames written: " << writer.frames_written() << "\n";
        std::cout << "Total bytes written: " << writer.bytes_written() << "\n";
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
    
    return 0;
}