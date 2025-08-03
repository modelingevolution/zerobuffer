#include <zerobuffer/zerobuffer.h>
#include <iostream>
#include <chrono>
#include <thread>

int main(int argc, char* argv[]) {
    if (argc != 4) {
        std::cerr << "Usage: " << argv[0] << " <buffer-name> <metadata-size> <payload-size>\n";
        std::cerr << "Example: " << argv[0] << " my-buffer 1024 1048576\n";
        return 1;
    }
    
    std::string buffer_name = argv[1];
    size_t metadata_size = std::stoull(argv[2]);
    size_t payload_size = std::stoull(argv[3]);
    
    try {
        std::cout << "Creating ZeroBuffer reader:\n";
        std::cout << "  Name: " << buffer_name << "\n";
        std::cout << "  Metadata size: " << metadata_size << " bytes\n";
        std::cout << "  Payload size: " << payload_size << " bytes\n\n";
        
        zerobuffer::BufferConfig config(metadata_size, payload_size);
        zerobuffer::Reader reader(buffer_name, config);
        
        std::cout << "Reader created. Waiting for writer...\n";
        
        // Wait for writer
        while (!reader.is_writer_connected()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
        
        std::cout << "Writer connected!\n";
        
        // Get metadata if available
        auto metadata = reader.get_metadata();
        if (!metadata.empty()) {
            std::cout << "Received metadata: " << metadata.size() << " bytes\n";
        }
        
        // Read frames continuously
        std::cout << "\nReading frames... (Press Ctrl+C to stop)\n";
        
        uint64_t total_frames = 0;
        auto start_time = std::chrono::steady_clock::now();
        
        while (true) {
            try {
                zerobuffer::Frame frame = reader.read_frame();
                
                if (frame.valid()) {
                    total_frames++;
                    
                    // Print stats every 100 frames
                    if (total_frames % 100 == 0) {
                        auto now = std::chrono::steady_clock::now();
                        auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(now - start_time).count();
                        
                        std::cout << "Frames: " << total_frames 
                                  << ", Latest seq: " << frame.sequence()
                                  << ", Size: " << frame.size() 
                                  << " bytes, Rate: " << (elapsed > 0 ? total_frames / elapsed : 0) 
                                  << " fps\n";
                    }
                    
                    reader.release_frame(frame);
                }
            } catch (const zerobuffer::WriterDeadException&) {
                std::cout << "\nWriter disconnected. Exiting.\n";
                break;
            }
        }
        
        std::cout << "\nTotal frames read: " << reader.frames_read() << "\n";
        std::cout << "Total bytes read: " << reader.bytes_read() << "\n";
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
    
    return 0;
}