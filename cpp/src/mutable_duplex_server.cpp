#include "zerobuffer/mutable_duplex_server.h"
#include "zerobuffer/logger.h"
#include <cstring>
#include <stdexcept>
#include <chrono>
#include <vector>

namespace zerobuffer {

MutableDuplexServer::MutableDuplexServer(const std::string& channel_name, const BufferConfig& config)
    : channel_name_(channel_name)
    , config_(config)
{
}

MutableDuplexServer::~MutableDuplexServer() {
    stop();
}

void MutableDuplexServer::start(std::function<void(Frame&)> handler, bool is_async) {
    if (is_running_.exchange(true)) {
        throw std::runtime_error("Server is already running");
    }
    
    if (!handler) {
        throw std::invalid_argument("Handler cannot be null");
    }
    
    // Channel naming convention:
    const std::string request_buffer_name = channel_name_ + "_request";
    const std::string response_buffer_name = channel_name_ + "_response";
    
    try {
        // Create request buffer as reader (we own this)
        request_reader_ = std::make_unique<Reader>(request_buffer_name, config_);
        
        should_stop_ = false;
        
        // Start processing thread - it will connect to response buffer when available
        processing_thread_ = std::thread([this, handler, response_buffer_name]() {
            process_requests(handler, response_buffer_name);
        });
    }
    catch (...) {
        stop();
        throw;
    }
}

void MutableDuplexServer::stop() {
    if (!is_running_.exchange(false)) {
        return;
    }
    
    should_stop_ = true;
    
    // Wait for processing thread to complete
    if (processing_thread_.joinable()) {
        processing_thread_.join();
    }
    
    // Clean up resources
    response_writer_.reset();
    request_reader_.reset();
}

bool MutableDuplexServer::is_running() const {
    return is_running_;
}

std::unique_ptr<Writer> MutableDuplexServer::connect_to_response_buffer(const std::string& buffer_name) {
    constexpr int max_retries = 50; // 5 second timeout
    constexpr auto retry_delay = std::chrono::milliseconds(100);
    
    for (int i = 0; i < max_retries; ++i) {
        try {
            return std::make_unique<Writer>(buffer_name);
        }
        catch (const std::exception&) {
            std::this_thread::sleep_for(retry_delay);
        }
    }
    
    throw std::runtime_error("Timeout waiting for response buffer " + buffer_name);
}

void MutableDuplexServer::process_requests(std::function<void(Frame&)> handler, const std::string& response_buffer_name) {
    // Connect to response buffer when it becomes available
    if (!response_writer_) {
        try {
            response_writer_ = connect_to_response_buffer(response_buffer_name);
        }
        catch (const std::exception& ex) {
            ZEROBUFFER_LOG_ERROR(channel_name_) << "Failed to connect to response buffer: " << ex.what();
            return;
        }
    }
    
    while (!should_stop_ && is_running_) {
        try {
            // Read request with timeout
            Frame request = request_reader_->read_frame(std::chrono::seconds(1));
            if (!request.is_valid()) {
                // Timeout or invalid frame - check if we should stop
                if (should_stop_) {
                    break;
                }
                continue;
            }
            
            // For mutable processing, we need to make a copy because:
            // 1. The request is in the request buffer (read-only for us)
            // 2. We need to send the response to a different buffer
            const uint8_t* request_data_ptr = static_cast<const uint8_t*>(request.data());
            std::vector<uint8_t> response_data(request_data_ptr, request_data_ptr + request.size());
            
            // Let handler modify the response data
            try {
                // Create a frame wrapper around the response data
                Frame mutable_frame(response_data.data(), response_data.size(), request.sequence());
                handler(mutable_frame);
            }
            catch (const std::exception& ex) {
                // Log error but continue processing
                ZEROBUFFER_LOG_ERROR(channel_name_) << "Handler error: " << ex.what();
            }
            
            // Send the modified data as response with sequence number prefix
            const size_t total_size = sizeof(uint64_t) + response_data.size();
            uint64_t frame_sequence;
            void* buffer = response_writer_->get_frame_buffer(total_size, frame_sequence);
            
            // Write sequence number
            const uint64_t sequence = request.sequence();
            std::memcpy(buffer, &sequence, sizeof(sequence));
            
            // Copy response data
            if (!response_data.empty()) {
                std::memcpy(static_cast<uint8_t*>(buffer) + sizeof(uint64_t), 
                           response_data.data(), response_data.size());
            }
            
            // Commit the frame
            response_writer_->commit_frame();
        }
        catch (const std::exception& ex) {
            // Log unexpected errors unless we're shutting down
            if (!should_stop_) {
                ZEROBUFFER_LOG_ERROR(channel_name_) << "Server processing error: " << ex.what();
            } else {
                ZEROBUFFER_LOG_DEBUG("MutableDuplexServer") << "Server stopped: " << ex.what();
            }
        }
    }
}

} // namespace zerobuffer