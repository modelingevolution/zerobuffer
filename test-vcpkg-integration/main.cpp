#include <zerobuffer/zerobuffer.h>
#include <iostream>

int main() {
    try {
        zerobuffer::BufferConfig config(1024, 1024*1024);
        std::cout << "✓ ZeroBuffer loaded from custom registry!" << std::endl;
        std::cout << "  Metadata size: " << config.metadata_size << std::endl;
        std::cout << "  Payload size: " << config.payload_size << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "✗ Error: " << e.what() << std::endl;
        return 1;
    }
}
