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
class IDuplexChannelFactory;
class Writer;
class Reader;

// Client-side interface for duplex communication
class IDuplexClient {
public:
    virtual ~IDuplexClient() = default;
    
    // Write data to request channel, returns sequence number from Writer
    virtual uint64_t write(const void* data, size_t size) = 0;
    
    // Write data using span
    virtual uint64_t write(std::span<const uint8_t> data) {
        return write(data.data(), data.size());
    }
    
    // Acquire buffer for zero-copy write
    virtual std::span<uint8_t> acquire_buffer(size_t size) = 0;
    
    // Commit the buffer after writing, returns sequence number
    virtual uint64_t commit() = 0;
    
    // Read next frame from response channel
    virtual Frame read(std::chrono::milliseconds timeout) = 0;
    
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

// Handler function that receives request Frame and Writer for response
using ImmutableHandler = std::function<void(Frame request, Writer& response_writer)>;

// Server that processes immutable requests
class IImmutableDuplexServer : public IDuplexServer {
public:
    // Start processing requests with handler that has direct Writer access
    virtual void start(const ImmutableHandler& handler) = 0;
};

// Mutable server will be available in v2.0.0.0 with shared payload buffer support
// For now, only immutable server is supported

// Factory interface for creating duplex channels
class IDuplexChannelFactory {
public:
    virtual ~IDuplexChannelFactory() = default;
    
    // Create an immutable server
    virtual std::unique_ptr<IImmutableDuplexServer> create_immutable_server(
        const std::string& channel_name,
        const BufferConfig& config) = 0;
    
    // Create a client with optional response buffer configuration
    virtual std::unique_ptr<IDuplexClient> create_client(
        const std::string& channel_name,
        const BufferConfig& response_config = BufferConfig(4096, 256*1024*1024)) = 0;
    
    // Note: Mutable server will be available in v2.0.0.0
};

} // namespace zerobuffer