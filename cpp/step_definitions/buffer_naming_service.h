#ifndef ZEROBUFFER_BUFFER_NAMING_SERVICE_H
#define ZEROBUFFER_BUFFER_NAMING_SERVICE_H

#include <string>
#include <unordered_map>
#include <zerobuffer/logger.h>

namespace zerobuffer {
namespace steps {

/**
 * Service for generating unique buffer names for test isolation
 * 
 * Ensures unique buffer names across test runs to prevent conflicts
 * when multiple tests run in parallel. Follows the same pattern as
 * C# and Python implementations for cross-platform consistency.
 */
class BufferNamingService {
public:
    /**
     * Initialize the buffer naming service
     * Checks for Harmony environment variables and generates a unique test run ID
     */
    BufferNamingService();
    
    /**
     * Get a unique buffer name for the given base name
     * 
     * @param baseName The base buffer name from the test
     * @return A unique buffer name that includes the test run ID
     */
    std::string getBufferName(const std::string& baseName);
    
    /**
     * Clear the name cache (useful for test cleanup)
     */
    void clearCache();
    
private:
    std::unordered_map<std::string, std::string> nameCache_;
    std::string testRunId_;
    
    void initializeTestRunId();
};

} // namespace steps
} // namespace zerobuffer

#endif // ZEROBUFFER_BUFFER_NAMING_SERVICE_H