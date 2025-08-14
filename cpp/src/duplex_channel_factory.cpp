#include "zerobuffer/duplex_channel_factory.h"
#include "zerobuffer/duplex_client.h"
#include "zerobuffer/immutable_duplex_server.h"
// MutableDuplexServer will be included in v2.0.0.0

namespace zerobuffer {

std::unique_ptr<IImmutableDuplexServer> DuplexChannelFactory::create_immutable_server(
    const std::string& channel_name,
    const BufferConfig& config) {
    return std::make_unique<ImmutableDuplexServer>(channel_name, config);
}

std::unique_ptr<IDuplexClient> DuplexChannelFactory::create_client(
    const std::string& channel_name,
    const BufferConfig& response_config) {
    return std::make_unique<DuplexClient>(channel_name, response_config);
}

} // namespace zerobuffer