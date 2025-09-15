#include "zerobuffer/duplex_client.h"
#include "zerobuffer/logger.h"
#include <cstring>
#include <stdexcept>
#ifndef _WIN32
#include <sys/stat.h>
#endif

namespace zerobuffer {

DuplexClient::DuplexClient(const std::string& channel_name, const BufferConfig& response_config)
    : channel_name_(channel_name)
    , response_config_(response_config)
{
    // Channel naming convention:
    // Request: {channel_name}_request (client writes, server reads)
    // Response: {channel_name}_response (server writes, client reads)
    const std::string request_buffer_name = channel_name + "_request";
    const std::string response_buffer_name = channel_name + "_response";
    
    ZEROBUFFER_LOG_INFO("DuplexClient") << "Creating client for channel " << channel_name;
    ZEROBUFFER_LOG_DEBUG("DuplexClient") << "Request buffer: " << request_buffer_name;
    ZEROBUFFER_LOG_DEBUG("DuplexClient") << "Response buffer: " << response_buffer_name;
    
    try {
        // Connect to request buffer as writer (server creates this)
        request_writer_ = std::make_unique<Writer>(request_buffer_name);
        
        // Create response buffer as reader (we own this)
#ifndef _WIN32
        // Set umask to 0 temporarily to ensure the buffer is accessible by all
        mode_t old_umask = umask(0);
        try {
            response_reader_ = std::make_unique<Reader>(response_buffer_name, response_config_);
        } catch (...) {
            umask(old_umask);  // Restore umask before rethrowing
            throw;
        }
        umask(old_umask);  // Restore original umask
#else
        // On Windows, permissions are handled differently
        response_reader_ = std::make_unique<Reader>(response_buffer_name, response_config_);
#endif
    }
    catch (...) {
        // Clean up if construction fails
        request_writer_.reset();
        response_reader_.reset();
        throw;
    }
}

DuplexClient::~DuplexClient() = default;

// write() method has been removed as it's inefficient
// Use acquire_buffer() and commit() for zero-copy operations

std::span<uint8_t> DuplexClient::acquire_buffer(size_t size) {
    if (!request_writer_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    // Get frame buffer and store sequence number for commit
    void* buffer = request_writer_->get_frame_buffer(size, pending_sequence_);
    
    return std::span<uint8_t>(static_cast<uint8_t*>(buffer), size);
}

uint64_t DuplexClient::commit() {
    if (!request_writer_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    request_writer_->commit_frame();
    return pending_sequence_;
}

Frame DuplexClient::read(std::chrono::milliseconds timeout) {
    if (!response_reader_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    // Read and return frame directly
    return response_reader_->read_frame(timeout);
}

bool DuplexClient::is_server_connected() const {
    return request_writer_ && request_writer_->is_reader_connected();
}

void DuplexClient::set_metadata(const void* data, size_t size) {
    if (!request_writer_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    request_writer_->set_metadata(data, size);
}

} // namespace zerobuffer