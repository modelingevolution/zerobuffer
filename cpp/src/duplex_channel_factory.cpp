#include "zerobuffer/duplex_channel_factory.h"
#include "zerobuffer/duplex_client.h"
#include "zerobuffer/immutable_duplex_server.h"
#include "zerobuffer/mutable_duplex_server.h"

namespace zerobuffer {

std::unique_ptr<IImmutableDuplexServer> DuplexChannelFactory::create_immutable_server(
    const std::string& channel_name,
    const BufferConfig& config) {
    return std::make_unique<ImmutableDuplexServer>(channel_name, config);
}

std::unique_ptr<IMutableDuplexServer> DuplexChannelFactory::create_mutable_server(
    const std::string& channel_name,
    const BufferConfig& config) {
    return std::make_unique<MutableDuplexServer>(channel_name, config);
}

std::unique_ptr<IDuplexClient> DuplexChannelFactory::create_client(
    const std::string& channel_name) {
    return std::make_unique<DuplexClient>(channel_name);
}

} // namespace zerobuffer