#include "buffer_naming_service.h"
#include <cstdlib>
#include <chrono>
#include <unistd.h>
#include <sstream>

namespace zerobuffer {
namespace steps {

BufferNamingService::BufferNamingService() {
    initializeTestRunId();
}

void BufferNamingService::initializeTestRunId() {
    // Check for Harmony environment variables
    const char* harmonyPid = std::getenv("HARMONY_HOST_PID");
    const char* harmonyFeatureId = std::getenv("HARMONY_FEATURE_ID");
    
    if (harmonyPid && harmonyFeatureId) {
        // Running under Harmony - use provided values for resource isolation
        std::stringstream ss;
        ss << harmonyPid << "_" << harmonyFeatureId;
        testRunId_ = ss.str();
        ZEROBUFFER_LOG_DEBUG("BufferNamingService") 
            << "Initialized with Harmony test run ID: " << testRunId_;
    } else {
        // Running standalone - use process ID and timestamp for uniqueness
        pid_t pid = getpid();
        auto now = std::chrono::high_resolution_clock::now();
        auto timestamp = std::chrono::duration_cast<std::chrono::nanoseconds>(
            now.time_since_epoch()).count();
        
        std::stringstream ss;
        ss << pid << "_" << timestamp;
        testRunId_ = ss.str();
        ZEROBUFFER_LOG_DEBUG("BufferNamingService") 
            << "Initialized with standalone test run ID: " << testRunId_;
    }
}

std::string BufferNamingService::getBufferName(const std::string& baseName) {
    // Return cached name if we've seen this base name before
    auto it = nameCache_.find(baseName);
    if (it != nameCache_.end()) {
        // Uncomment for verbose debugging:
        // ZEROBUFFER_LOG_DEBUG("BufferNamingService") 
        //     << "Returning cached buffer name: " << it->second 
        //     << " for base name: " << baseName;
        return it->second;
    }
    
    // Create new unique name and cache it
    std::stringstream ss;
    ss << baseName << "_" << testRunId_;
    std::string uniqueName = ss.str();
    nameCache_[baseName] = uniqueName;
    
    // Uncomment for verbose debugging:
    // ZEROBUFFER_LOG_DEBUG("BufferNamingService") 
    //     << "Created and cached buffer name: " << uniqueName 
    //     << " for base name: " << baseName;
    
    return uniqueName;
}

void BufferNamingService::clearCache() {
    nameCache_.clear();
    ZEROBUFFER_LOG_DEBUG("BufferNamingService") << "Cleared buffer name cache";
}

} // namespace steps
} // namespace zerobuffer