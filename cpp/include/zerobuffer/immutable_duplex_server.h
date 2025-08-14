#pragma once

#include "duplex_channel.h"
#include "reader.h"
#include "writer.h"
#include <memory>
#include <thread>
#include <atomic>

namespace zerobuffer {

class ImmutableDuplexServer : public IImmutableDuplexServer {
public:
    ImmutableDuplexServer(const std::string& channel_name, const BufferConfig& config);
    ~ImmutableDuplexServer() override;
    
    // IImmutableDuplexServer implementation
    void start(const ImmutableHandler& handler) override;
    
    // IDuplexServer implementation
    void stop() override;
    bool is_running() const override;
    
private:
    void process_requests(const ImmutableHandler& handler, const std::string& response_buffer_name);
    std::unique_ptr<Writer> connect_to_response_buffer(const std::string& buffer_name);
    
    std::string channel_name_;
    BufferConfig config_;
    std::unique_ptr<Reader> request_reader_;
    std::unique_ptr<Writer> response_writer_;
    std::thread processing_thread_;
    std::atomic<bool> is_running_{false};
    std::atomic<bool> should_stop_{false};
};

} // namespace zerobuffer