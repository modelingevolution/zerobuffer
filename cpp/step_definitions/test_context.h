#ifndef ZEROBUFFER_TEST_CONTEXT_H
#define ZEROBUFFER_TEST_CONTEXT_H

#include <zerobuffer/reader.h>
#include <zerobuffer/writer.h>
#include <zerobuffer/types.h>
#include <nlohmann/json.hpp>
#include "buffer_naming_service.h"

#include <memory>
#include <unordered_map>
#include <string>
#include <exception>
#include <mutex>

namespace zerobuffer {
namespace steps {

using json = nlohmann::json;

/**
 * TestContext manages state across test steps
 * 
 * This class provides:
 * - Storage for Reader/Writer instances per process
 * - Property storage for test data
 * - Exception capture for expected failures
 * - Current buffer tracking
 */
class TestContext {
public:
    TestContext();
    ~TestContext() = default;
    
    // Delete copy constructor, allow move
    TestContext(const TestContext&) = delete;
    TestContext& operator=(const TestContext&) = delete;
    TestContext(TestContext&&) = default;
    TestContext& operator=(TestContext&&) = default;
    
    // Reader/Writer management
    void createReader(const std::string& processName, 
                     const std::string& bufferName,
                     const BufferConfig& config);
    
    void createWriter(const std::string& processName,
                     const std::string& bufferName);
    
    Reader* getReader(const std::string& processName);
    Writer* getWriter(const std::string& processName);
    
    bool hasReader(const std::string& processName) const;
    bool hasWriter(const std::string& processName) const;
    
    // Buffer management
    void setCurrentBuffer(const std::string& bufferName);
    std::string getCurrentBuffer() const;
    
    // Property storage
    void setProperty(const std::string& key, const json& value);
    json getProperty(const std::string& key) const;
    bool hasProperty(const std::string& key) const;
    
    // Step parameter access (stored with "param:" prefix)
    void setParameter(const std::string& key, const json& value);
    json getParameter(const std::string& key) const;
    bool hasParameter(const std::string& key) const;
    void clearParameters();  // Clear all parameters before next step
    
    // Initialization context (from Harmony InitializeRequest)
    void setInitializationContext(const std::string& role, 
                                   const std::string& platform,
                                   const std::string& scenario,
                                   int hostPid,
                                   int featureId);
    std::string getRole() const;
    std::string getPlatform() const;
    std::string getScenario() const;
    int getHostPid() const;
    int getFeatureId() const;
    std::string getTestRunId() const; // Returns "hostPid_featureId"
    
    // Exception handling for expected failures
    void setLastException(std::exception_ptr ex);
    std::exception_ptr getLastException() const;
    bool hasException() const;
    void clearException();
    
    // Frame storage for verification
    void setLastFrame(const Frame& frame);
    Frame getLastFrame() const;
    bool hasLastFrame() const;
    
    // Reset context between tests
    void reset();
    
    // Get statistics
    size_t getReaderCount() const;
    size_t getWriterCount() const;
    
    // Get buffer naming service
    BufferNamingService& getBufferNaming() { return bufferNaming_; }
    
private:
    // Thread safety
    mutable std::mutex mutex_;
    
    // Buffer naming service for test isolation
    BufferNamingService bufferNaming_;
    
    // Process-based storage
    std::unordered_map<std::string, std::unique_ptr<Reader>> readers_;
    std::unordered_map<std::string, std::unique_ptr<Writer>> writers_;
    
    // Test state
    std::unordered_map<std::string, json> properties_;
    std::string currentBuffer_;
    
    // Initialization context from Harmony
    std::string role_;
    std::string platform_;
    std::string scenario_;
    int hostPid_ = 0;
    int featureId_ = 0;
    
    // Exception handling
    std::exception_ptr lastException_;
    
    // Frame storage (using heap allocation since Frame is stack-only)
    struct FrameData {
        std::vector<uint8_t> data;
        size_t size;
        bool valid;
    };
    std::unique_ptr<FrameData> lastFrame_;
};

} // namespace steps
} // namespace zerobuffer

#endif // ZEROBUFFER_TEST_CONTEXT_H