#include "test_context.h"
#include <zerobuffer/logger.h>
#include <stdexcept>

namespace zerobuffer {
namespace steps {

TestContext::TestContext() : bufferNaming_() {
    // Constructor initializes BufferNamingService
}

void TestContext::createReader(const std::string& processName, 
                               const std::string& bufferName,
                               const BufferConfig& config) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    if (readers_.find(processName) != readers_.end()) {
        throw std::runtime_error("Reader already exists for process: " + processName);
    }
    
    // Use BufferNamingService to get unique buffer name
    std::string actualBufferName = bufferNaming_.getBufferName(bufferName);
    
    readers_[processName] = std::make_unique<Reader>(actualBufferName, config);
    currentBuffer_ = bufferName;  // Store the original name for reference
    
    ZEROBUFFER_LOG_DEBUG("TestContext") << "Created reader for process '" << processName 
                                         << "' with buffer '" << actualBufferName 
                                         << "' (base name: '" << bufferName << "')";
}

void TestContext::createWriter(const std::string& processName,
                               const std::string& bufferName) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    if (writers_.find(processName) != writers_.end()) {
        throw std::runtime_error("Writer already exists for process: " + processName);
    }
    
    // Use BufferNamingService to get the same unique buffer name
    std::string actualBufferName = bufferNaming_.getBufferName(bufferName);
    
    writers_[processName] = std::make_unique<Writer>(actualBufferName);
    currentBuffer_ = bufferName;  // Store the original name for reference
    
    ZEROBUFFER_LOG_DEBUG("TestContext") << "Created writer for process '" << processName 
                                         << "' with buffer '" << actualBufferName 
                                         << "' (base name: '" << bufferName << "')";
}

Reader* TestContext::getReader(const std::string& processName) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    auto it = readers_.find(processName);
    if (it != readers_.end()) {
        return it->second.get();
    }
    return nullptr;
}

Writer* TestContext::getWriter(const std::string& processName) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    auto it = writers_.find(processName);
    if (it != writers_.end()) {
        return it->second.get();
    }
    return nullptr;
}

bool TestContext::hasReader(const std::string& processName) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return readers_.find(processName) != readers_.end();
}

bool TestContext::hasWriter(const std::string& processName) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return writers_.find(processName) != writers_.end();
}

void TestContext::setCurrentBuffer(const std::string& bufferName) {
    std::lock_guard<std::mutex> lock(mutex_);
    currentBuffer_ = bufferName;
}

std::string TestContext::getCurrentBuffer() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return currentBuffer_;
}

void TestContext::setProperty(const std::string& key, const json& value) {
    std::lock_guard<std::mutex> lock(mutex_);
    properties_[key] = value;
}

json TestContext::getProperty(const std::string& key) const {
    std::lock_guard<std::mutex> lock(mutex_);
    
    auto it = properties_.find(key);
    if (it != properties_.end()) {
        return it->second;
    }
    return json{};
}

bool TestContext::hasProperty(const std::string& key) const {
    std::lock_guard<std::mutex> lock(mutex_);
    return properties_.find(key) != properties_.end();
}

void TestContext::setLastException(std::exception_ptr ex) {
    std::lock_guard<std::mutex> lock(mutex_);
    lastException_ = ex;
}

std::exception_ptr TestContext::getLastException() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return lastException_;
}

bool TestContext::hasException() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return lastException_ != nullptr;
}

void TestContext::clearException() {
    std::lock_guard<std::mutex> lock(mutex_);
    lastException_ = nullptr;
}

void TestContext::setLastFrame(const Frame& frame) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    if (!lastFrame_) {
        lastFrame_ = std::make_unique<FrameData>();
    }
    
    // Copy frame data since Frame is stack-only
    lastFrame_->valid = frame.valid();
    lastFrame_->size = frame.size();
    if (frame.valid() && frame.size() > 0) {
        lastFrame_->data.assign(
            static_cast<const uint8_t*>(frame.data()),
            static_cast<const uint8_t*>(frame.data()) + frame.size()
        );
    }
}

Frame TestContext::getLastFrame() const {
    std::lock_guard<std::mutex> lock(mutex_);
    
    // Note: This is a simplified approach since we can't return a real Frame
    // In practice, we'd need a different approach or store frame data differently
    Frame dummy;
    return dummy;
}

bool TestContext::hasLastFrame() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return lastFrame_ && lastFrame_->valid;
}

void TestContext::reset() {
    std::lock_guard<std::mutex> lock(mutex_);
    
    readers_.clear();
    writers_.clear();
    properties_.clear();
    currentBuffer_.clear();
    lastException_ = nullptr;
    lastFrame_.reset();
    bufferNaming_.clearCache();  // Clear buffer name cache for new test
    
    ZEROBUFFER_LOG_DEBUG("TestContext") << "Context reset";
}

size_t TestContext::getReaderCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return readers_.size();
}

size_t TestContext::getWriterCount() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return writers_.size();
}

} // namespace steps
} // namespace zerobuffer