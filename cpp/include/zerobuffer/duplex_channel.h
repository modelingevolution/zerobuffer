#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <span>
#include <string>
#include <chrono>
#include <utility>

#include "types.h"

namespace zerobuffer {

// Forward declarations
class IDuplexClient;
class IDuplexServer;
class IImmutableDuplexServer;
class IMutableDuplexServer;
class IDuplexChannelFactory;

// Response wrapper for duplex channel
struct DuplexResponse {
    Frame frame;
    
    bool is_valid() const noexcept { 
        return frame.is_valid() && frame.size() >= sizeof(uint64_t); 
    }
    
    uint64_t sequence() const noexcept {
        if (!is_valid()) return 0;
        return *reinterpret_cast<const uint64_t*>(frame.data());
    }
    
    std::span<const uint8_t> data() const noexcept {
        if (!is_valid() || frame.size() <= sizeof(uint64_t)) {
            return {};
        }
        const uint8_t* data_ptr = static_cast<const uint8_t*>(frame.data()) + sizeof(uint64_t);
        return std::span<const uint8_t>(data_ptr, frame.size() - sizeof(uint64_t));
    }
};

// Client-side interface for sending requests and receiving responses
class IDuplexClient {
public:
    virtual ~IDuplexClient() = default;
    
    // Send a request with data copy and return the sequence number
    virtual uint64_t send_request(const void* data, size_t size) = 0;
    
    // Send a request using span
    virtual uint64_t send_request(std::span<const uint8_t> data) {
        return send_request(data.data(), data.size());
    }
    
    // Acquire buffer for zero-copy write
    virtual std::pair<uint64_t, std::span<uint8_t>> acquire_request_buffer(size_t size) = 0;
    
    // Commit the request after writing to the acquired buffer
    virtual void commit_request() = 0;
    
    // Receive a response with timeout
    virtual DuplexResponse receive_response(std::chrono::milliseconds timeout) = 0;
    
    // Check if server is connected
    virtual bool is_server_connected() const = 0;
};

// Base server interface
class IDuplexServer {
public:
    virtual ~IDuplexServer() = default;
    
    // Stop processing
    virtual void stop() = 0;
    
    // Check if running
    virtual bool is_running() const = 0;
};

// Handler function that returns response data as span
using RequestHandler = std::function<std::span<const uint8_t>(const Frame&)>;

// Server that processes immutable requests and returns new response data
class IImmutableDuplexServer : public IDuplexServer {
public:
    // Start processing requests with handler
    virtual void start(RequestHandler handler, bool is_async = false) = 0;
};

// Server that mutates request data in-place
class IMutableDuplexServer : public IDuplexServer {
public:
    // Start processing with mutable handler
    virtual void start(std::function<void(Frame&)> handler, bool is_async = false) = 0;
};

// Factory interface for creating duplex channels
class IDuplexChannelFactory {
public:
    virtual ~IDuplexChannelFactory() = default;
    
    // Create an immutable server
    virtual std::unique_ptr<IImmutableDuplexServer> create_immutable_server(
        const std::string& channel_name,
        const BufferConfig& config) = 0;
    
    // Create a mutable server
    virtual std::unique_ptr<IMutableDuplexServer> create_mutable_server(
        const std::string& channel_name,
        const BufferConfig& config) = 0;
    
    // Connect to existing duplex channel
    virtual std::unique_ptr<IDuplexClient> create_client(
        const std::string& channel_name) = 0;
};

} // namespace zerobuffer