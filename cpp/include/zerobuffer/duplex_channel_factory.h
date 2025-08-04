#pragma once

#include "duplex_channel.h"

namespace zerobuffer {

class DuplexChannelFactory : public IDuplexChannelFactory {
public:
    DuplexChannelFactory() = default;
    ~DuplexChannelFactory() override = default;
    
    // IDuplexChannelFactory implementation
    std::unique_ptr<IImmutableDuplexServer> create_immutable_server(
        const std::string& channel_name,
        const BufferConfig& config) override;
    
    std::unique_ptr<IMutableDuplexServer> create_mutable_server(
        const std::string& channel_name,
        const BufferConfig& config) override;
    
    std::unique_ptr<IDuplexClient> create_client(
        const std::string& channel_name) override;
};

} // namespace zerobuffer