#include "zerobuffer/duplex_client.h"
#include "zerobuffer/logger.h"
#include <cstring>
#include <stdexcept>

namespace zerobuffer {

DuplexClient::DuplexClient(const std::string& channel_name)
    : channel_name_(channel_name)
    , config_(4096, 256 * 1024 * 1024) // 256MB buffer
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
        response_reader_ = std::make_unique<Reader>(response_buffer_name, config_);
    }
    catch (...) {
        // Clean up if construction fails
        request_writer_.reset();
        response_reader_.reset();
        throw;
    }
}

DuplexClient::~DuplexClient() = default;

uint64_t DuplexClient::send_request(const void* data, size_t size) {
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

std::pair<uint64_t, std::span<uint8_t>> DuplexClient::acquire_request_buffer(size_t size) {
    if (!request_writer_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    // Get frame buffer and sequence number
    uint64_t sequence_number;
    void* buffer = request_writer_->get_frame_buffer(size, sequence_number);
    
    return {sequence_number, std::span<uint8_t>(static_cast<uint8_t*>(buffer), size)};
}

void DuplexClient::commit_request() {
    if (!request_writer_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    request_writer_->commit_frame();
}

DuplexResponse DuplexClient::receive_response(std::chrono::milliseconds timeout) {
    if (!response_reader_) {
        throw std::runtime_error("DuplexClient has been disposed");
    }
    
    // Read response frame with timeout
    Frame frame = response_reader_->read_frame(timeout);
    
    // Wrap it in DuplexResponse
    return DuplexResponse{std::move(frame)};
}

bool DuplexClient::is_server_connected() const {
    return request_writer_ && request_writer_->is_reader_connected();
}

} // namespace zerobuffer