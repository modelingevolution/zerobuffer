#include "zerobuffer/immutable_duplex_server.h"
#include "zerobuffer/logger.h"
#include <cstring>
#include <stdexcept>
#include <chrono>

namespace zerobuffer {

ImmutableDuplexServer::ImmutableDuplexServer(const std::string& channel_name, const BufferConfig& config)
    : channel_name_(channel_name)
    , config_(config)
{
}

ImmutableDuplexServer::~ImmutableDuplexServer() {
    stop();
}

void ImmutableDuplexServer::start(RequestHandler handler, bool is_async) {
    if (is_running_.exchange(true)) {
        throw std::runtime_error("Server is already running");
    }
    
    if (!handler) {
        throw std::invalid_argument("Handler cannot be null");
    }
    
    // Channel naming convention:
    const std::string request_buffer_name = channel_name_ + "_request";
    const std::string response_buffer_name = channel_name_ + "_response";
    
    ZEROBUFFER_LOG_INFO("ImmutableDuplexServer") << "Starting server on channel " << channel_name_;
    ZEROBUFFER_LOG_DEBUG("ImmutableDuplexServer") << "Request buffer: " << request_buffer_name;
    ZEROBUFFER_LOG_DEBUG("ImmutableDuplexServer") << "Response buffer: " << response_buffer_name;
    
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

void ImmutableDuplexServer::stop() {
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

bool ImmutableDuplexServer::is_running() const {
    return is_running_;
}

std::unique_ptr<Writer> ImmutableDuplexServer::connect_to_response_buffer(const std::string& buffer_name) {
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

void ImmutableDuplexServer::process_requests(RequestHandler handler, const std::string& response_buffer_name) {
    ZEROBUFFER_LOG_INFO("ImmutableDuplexServer") << "Processing thread started for channel " << channel_name_;
    
    // Connect to response buffer when it becomes available
    if (!response_writer_) {
        try {
            ZEROBUFFER_LOG_DEBUG("ImmutableDuplexServer") << "Connecting to response buffer: " << response_buffer_name;
            response_writer_ = connect_to_response_buffer(response_buffer_name);
            ZEROBUFFER_LOG_INFO("ImmutableDuplexServer") << "Connected to response buffer";
        }
        catch (const std::exception& ex) {
            ZEROBUFFER_LOG_ERROR(channel_name_) << "Failed to connect to response buffer: " << ex.what();
            return;
        }
    }
    
    while (!should_stop_ && is_running_) {
        try {
            // Check if reader still exists (might be destroyed during stop)
            if (!request_reader_) {
                break;
            }
            
            // Read request with timeout
            ZEROBUFFER_LOG_TRACE("ImmutableDuplexServer") << "Waiting for request...";
            Frame request = request_reader_->read_frame(std::chrono::seconds(1));
            if (!request.is_valid()) {
                // Timeout or invalid frame - check if we should stop
                if (should_stop_) {
                    break;
                }
                continue;
            }
            ZEROBUFFER_LOG_DEBUG("ImmutableDuplexServer") << "Received request seq=" << request.sequence() << " size=" << request.size();
            
            // Process request and get response data
            std::span<const uint8_t> response_data;
            try {
                response_data = handler(request);
            }
            catch (const std::exception& ex) {
                // Log error and send empty response
                ZEROBUFFER_LOG_ERROR(channel_name_) << "Error in request handler: " << ex.what();
                response_data = {};
            }
            
            // Allocate buffer for sequence number + response data
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
                ZEROBUFFER_LOG_DEBUG("ImmutableDuplexServer") << "Server stopped: " << ex.what();
            }
        }
    }
}

} // namespace zerobuffer