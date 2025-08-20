#include "zerobuffer/duplex_client.h"
#include "zerobuffer/logger.h"
#include <cstring>
#include <stdexcept>

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
        response_reader_ = std::make_unique<Reader>(response_buffer_name, response_config_);
    }
    catch (...) {
        // Clean up if construction fails
        request_writer_.reset();
        response_reader_.reset();
        throw;
    }
}

DuplexClient::~DuplexClient() = default;

uint64_t DuplexClient::write(const void* data, size_t size) {
    if (!request_writer_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    if (!data && size > 0) {
        throw std::invalid_argument("data cannot be null when size > 0");
    }
    
    // Use zero-copy write to get the sequence number
    uint64_t sequence_number;
    void* buffer = request_writer_->get_frame_buffer(size, sequence_number);
    if (size > 0) {
        std::memcpy(buffer, data, size);
    }
    request_writer_->commit_frame();
    
    return sequence_number;
}

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