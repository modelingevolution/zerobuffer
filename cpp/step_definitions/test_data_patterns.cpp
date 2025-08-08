#include "test_data_patterns.h"

namespace zerobuffer {
namespace steps {

std::vector<uint8_t> TestDataPatterns::generateFrameData(size_t size, uint64_t sequence) {
    std::vector<uint8_t> data(size);
    for (size_t i = 0; i < size; ++i) {
        data[i] = static_cast<uint8_t>((i + sequence) % 256);
    }
    return data;
}

std::vector<uint8_t> TestDataPatterns::generateSimpleFrameData(size_t size) {
    std::vector<uint8_t> data(size);
    for (size_t i = 0; i < size; ++i) {
        data[i] = static_cast<uint8_t>(i % 256);
    }
    return data;
}

bool TestDataPatterns::verifySimpleFrameData(const uint8_t* data, size_t size) {
    for (size_t i = 0; i < size; ++i) {
        if (data[i] != static_cast<uint8_t>(i % 256)) {
            return false;
        }
    }
    return true;
}

bool TestDataPatterns::verifySimpleFrameData(const std::vector<uint8_t>& data) {
    return verifySimpleFrameData(data.data(), data.size());
}

std::vector<uint8_t> TestDataPatterns::generateMetadata(size_t size) {
    std::vector<uint8_t> metadata(size);
    for (size_t i = 0; i < size; ++i) {
        metadata[i] = static_cast<uint8_t>(i % 256);
    }
    return metadata;
}

} // namespace steps
} // namespace zerobuffer