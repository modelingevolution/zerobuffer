#pragma once

#include "duplex_channel.h"
#include "writer.h"
#include "reader.h"
#include <memory>

namespace zerobuffer {

class DuplexClient : public IDuplexClient {
public:
    explicit DuplexClient(const std::string& channel_name, 
                         const BufferConfig& response_config = BufferConfig(4096, 256*1024*1024));
    ~DuplexClient() override;
    
    // IDuplexClient implementation
    uint64_t write(const void* data, size_t size) override;
    std::span<uint8_t> acquire_buffer(size_t size) override;
    uint64_t commit() override;
    Frame read(std::chrono::milliseconds timeout) override;
    bool is_server_connected() const override;
    void set_metadata(const void* data, size_t size) override;
    
private:
    std::string channel_name_;
    std::unique_ptr<Writer> request_writer_;
    std::unique_ptr<Reader> response_reader_;
    BufferConfig response_config_;
    
    // For zero-copy write tracking
    uint64_t pending_sequence_ = 0;
};

} // namespace zerobuffer