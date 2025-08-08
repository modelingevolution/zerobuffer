#ifndef ZEROBUFFER_TEST_DATA_PATTERNS_H
#define ZEROBUFFER_TEST_DATA_PATTERNS_H

#include <vector>
#include <cstdint>
#include <cstddef>

namespace zerobuffer {
namespace steps {

/**
 * Shared test data patterns for consistent data generation across processes
 * 
 * This class provides the same test data generation patterns as the C# and Python
 * implementations to ensure consistent data across all test processes.
 */
class TestDataPatterns {
public:
    /**
     * Generate test data for a frame based on size and sequence number
     * 
     * @param size Size of the frame data in bytes
     * @param sequence Sequence number of the frame
     * @return Generated frame data as vector of bytes
     */
    static std::vector<uint8_t> generateFrameData(size_t size, uint64_t sequence);
    
    /**
     * Generate simple test data for a frame based only on size
     * Used when sequence number is not known at write time
     * 
     * @param size Size of the frame data in bytes
     * @return Generated frame data as vector of bytes
     */
    static std::vector<uint8_t> generateSimpleFrameData(size_t size);
    
    /**
     * Verify that frame data matches the simple pattern
     * 
     * @param data Frame data to verify
     * @param size Size of the data
     * @return true if data matches the simple pattern, false otherwise
     */
    static bool verifySimpleFrameData(const uint8_t* data, size_t size);
    
    /**
     * Verify that frame data matches the simple pattern
     * 
     * @param data Frame data to verify as vector
     * @return true if data matches the simple pattern, false otherwise
     */
    static bool verifySimpleFrameData(const std::vector<uint8_t>& data);
    
    /**
     * Generate test metadata based on size
     * 
     * @param size Size of the metadata in bytes
     * @return Generated metadata as vector of bytes
     */
    static std::vector<uint8_t> generateMetadata(size_t size);
};

} // namespace steps
} // namespace zerobuffer

#endif // ZEROBUFFER_TEST_DATA_PATTERNS_H