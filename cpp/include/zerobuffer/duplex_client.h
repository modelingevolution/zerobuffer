#pragma once

#include "duplex_channel.h"
#include "writer.h"
#include "reader.h"
#include <memory>

namespace zerobuffer {

class DuplexClient : public IDuplexClient {
public:
    explicit DuplexClient(const std::string& channel_name);
    ~DuplexClient() override;
    
    // IDuplexClient implementation
    uint64_t send_request(const void* data, size_t size) override;
    std::pair<uint64_t, std::span<uint8_t>> acquire_request_buffer(size_t size) override;
    void commit_request() override;
    DuplexResponse receive_response(std::chrono::milliseconds timeout) override;
    bool is_server_connected() const override;
    
private:
    std::string channel_name_;
    std::unique_ptr<Writer> request_writer_;
    std::unique_ptr<Reader> response_reader_;
    BufferConfig config_;
};

} // namespace zerobuffer