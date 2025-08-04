#include <zerobuffer/zerobuffer.h>
#include <iostream>
#include <cstring>
#include <chrono>

// Example custom metadata structure
struct VideoMetadata {
    uint32_t width;
    uint32_t height;
    uint32_t fps;
    uint32_t format;  // Could be fourcc
    char codec[32];
};

int main() {
    const std::string buffer_name = "metadata-example";
    
    // Writer process
    {
        zerobuffer::Reader reader(buffer_name, zerobuffer::BufferConfig(256, 1024 * 1024));
        zerobuffer::Writer writer(buffer_name);
        
        // Create and set metadata
        VideoMetadata meta;
        meta.width = 1920;
        meta.height = 1080;
        meta.fps = 30;
        meta.format = 0x32595559; // YUY2
        std::strncpy(meta.codec, "H.264", sizeof(meta.codec));
        
        writer.set_metadata(&meta, sizeof(meta));
        std::cout << "Writer: Set video metadata\n";
        
        // Write a frame
        std::vector<uint8_t> frame(100, 0x42);
        writer.write_frame(frame);
    }
    
    // Reader process (could be separate)
    {
        zerobuffer::Reader reader(buffer_name, zerobuffer::BufferConfig(256, 1024 * 1024));
        
        // Get metadata using zero-copy access
        const VideoMetadata* meta = reader.get_metadata_as<VideoMetadata>();
        if (meta) {
            std::cout << "Reader: Got metadata (zero-copy):\n";
            std::cout << "  Resolution: " << meta->width << "x" << meta->height << "\n";
            std::cout << "  FPS: " << meta->fps << "\n";
            std::cout << "  Codec: " << meta->codec << "\n";
            std::cout << "  Metadata size: " << reader.get_metadata_size() << " bytes\n";
        }
        
        // Alternative: Get metadata as bytes (makes a copy)
        auto metadata_bytes = reader.get_metadata();
        std::cout << "Reader: Got metadata copy, size=" << metadata_bytes.size() << "\n";
        
        // Read frame
        zerobuffer::Frame frame = reader.read_frame(std::chrono::seconds(5));
        std::cout << "Reader: Got frame, size=" << frame.size() << "\n";
        reader.release_frame(frame);
    }
    
    return 0;
}