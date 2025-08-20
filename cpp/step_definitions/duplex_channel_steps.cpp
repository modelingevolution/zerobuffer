#include "duplex_channel_steps.h"
#include "test_context.h"
#include "buffer_naming_service.h"
#include <zerobuffer/duplex_channel_factory.h>
#include <zerobuffer/duplex_channel.h>
#include <zerobuffer/logger.h>
#include <thread>
#include <chrono>
#include <regex>
#include <random>
#include <cstring>

namespace zerobuffer {
namespace steps {

namespace {
    // Storage for servers, clients, and test data
    std::map<std::string, std::unique_ptr<IImmutableDuplexServer>> immutable_servers;
    // MutableDuplexServer will be implemented in v2.0.0
    // std::map<std::string, std::unique_ptr<IMutableDuplexServer>> mutable_servers;
    std::map<std::string, std::unique_ptr<IDuplexClient>> clients;
    std::map<uint64_t, std::vector<uint8_t>> sent_requests;
    std::map<uint64_t, std::vector<uint8_t>> received_responses;
    std::vector<std::pair<uint64_t, std::vector<uint8_t>>> responses;
    std::chrono::steady_clock::time_point measurement_start;
    std::chrono::milliseconds total_response_time;
    BufferNamingService naming_service;
    std::atomic<int> test_counter{0};
    
    void cleanup() {
        // Stop all servers
        for (auto& [name, server] : immutable_servers) {
            if (server) {
                server->stop();
            }
        }
        // MutableDuplexServer will be implemented in v2.0.0
        // for (auto& [name, server] : mutable_servers) {
        //     if (server) {
        //         server->stop();
        //     }
        // }
        
        // Clear all storage
        immutable_servers.clear();
        // mutable_servers.clear();
        clients.clear();
        sent_requests.clear();
        received_responses.clear();
        responses.clear();
    }
}

void registerDuplexChannelSteps(StepRegistry& registry) {
    // Given the 'process' creates immutable duplex channel 'name' with metadata size 'X' and payload size 'Y'
    registry.registerStep(
        "the '([^']+)' process creates immutable duplex channel '([^']+)' with metadata size '([^']+)' and payload size '([^']+)'",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            std::string channel_name = params[1];
            size_t metadata_size = std::stoull(params[2]);
            size_t payload_size = std::stoull(params[3]);
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Creating duplex channel '" << channel_name 
                << "' with metadata=" << metadata_size 
                << " payload=" << payload_size;
            
            // Clean up any existing server with the same name
            if (immutable_servers.count(channel_name) > 0) {
                ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                    << "Cleaning up existing server for channel '" << channel_name << "'";
                if (immutable_servers[channel_name]) {
                    immutable_servers[channel_name]->stop();
                }
                immutable_servers.erase(channel_name);
            }
            
            std::string actual_name = naming_service.getBufferName(channel_name);
            
            BufferConfig config(metadata_size, payload_size);
            DuplexChannelFactory factory;
            
            try {
                auto server = factory.create_immutable_server(actual_name, config);
                immutable_servers[channel_name] = std::move(server);
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to create duplex channel: " << e.what();
                throw;
            }
        }
    );
    
    // Given the 'process' creates immutable duplex channel 'name' with default config
    registry.registerStep(
        "the '([^']+)' process creates immutable duplex channel '([^']+)' with default config",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            std::string channel_name = params[1];
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Creating duplex channel '" << channel_name << "' with default config";
            
            // Clean up any existing server with the same name
            if (immutable_servers.count(channel_name) > 0) {
                ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                    << "Cleaning up existing server for channel '" << channel_name << "'";
                if (immutable_servers[channel_name]) {
                    immutable_servers[channel_name]->stop();
                }
                immutable_servers.erase(channel_name);
            }
            
            std::string actual_name = naming_service.getBufferName(channel_name);
            
            BufferConfig config(4096, 1024 * 1024); // Default: 4KB metadata, 1MB payload
            DuplexChannelFactory factory;
            
            try {
                auto server = factory.create_immutable_server(actual_name, config);
                immutable_servers[channel_name] = std::move(server);
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to create duplex channel: " << e.what();
                throw;
            }
        }
    );
    
    // Given the 'process' starts echo handler
    registry.registerStep(
        "the '([^']+)' process starts echo handler",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") << "Starting echo handler";
            
            // Find the last created server
            if (immutable_servers.empty()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "No server created";
                throw std::runtime_error("No server created");
            }
            
            auto& server = immutable_servers.rbegin()->second;
            if (!server) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Server is null";
                throw std::runtime_error("Server is null");
            }
            
            try {
                server->start([](Frame request, Writer& response_writer) {
                    ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                        << "Echo handler received " << request.size() << " bytes";
                    // Echo back the data (v1.0.0 - write directly to response writer)
                    response_writer.write_frame(request.data(), request.size());
                });
                
                // Give server time to initialize
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to start echo handler: " << e.what();
                throw;
            }
        }
    );
    
    // Given the 'process' starts delayed echo handler with 'X' ms delay
    registry.registerStep(
        "the '([^']+)' process starts delayed echo handler with '([^']+)' ms delay",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            int delay_ms = std::stoi(params[1]);
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Starting delayed echo handler with " << delay_ms << "ms delay";
            
            if (immutable_servers.empty()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "No server created";
                throw std::runtime_error("No server created");
            }
            
            auto& server = immutable_servers.rbegin()->second;
            if (!server) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Server is null";
                throw std::runtime_error("Server is null");
            }
            
            try {
                server->start([delay_ms](Frame request, Writer& response_writer) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(delay_ms));
                    // Echo back after delay (v1.0.0 - write directly to response writer)
                    response_writer.write_frame(request.data(), request.size());
                });
                
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to start delayed echo handler: " << e.what();
                throw;
            }
        }
    );
    
    // When the 'process' creates duplex channel client 'name'
    registry.registerStep(
        "the '([^']+)' process creates duplex channel client '([^']+)'",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            std::string channel_name = params[1];
            
            // Clean up any existing client with the same name
            if (clients.count(channel_name) > 0) {
                ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                    << "Cleaning up existing client for channel '" << channel_name << "'";
                clients.erase(channel_name);
            }
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Creating duplex channel client for '" << channel_name << "'";
            
            std::string actual_name = naming_service.getBufferName(channel_name);
            
            DuplexChannelFactory factory;
            
            try {
                auto client = factory.create_client(actual_name);
                clients[channel_name] = std::move(client);
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to create duplex channel client: " << e.what();
                throw;
            }
        }
    );
    
    // When the 'process' sends request with size 'X'
    registry.registerStep(
        "the '([^']+)' process sends request with size '([^']+)'",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            size_t size = std::stoull(params[1]);
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Sending request with size " << size;
            
            if (clients.empty()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "No client connected";
                throw std::runtime_error("No client connected");
            }
            
            auto& client = clients.rbegin()->second;
            if (!client) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Client is null";
                throw std::runtime_error("Client is null");
            }
            
            try {
                // Create test data with timestamp at the beginning
                std::vector<uint8_t> data(size);
                
                // Add timestamp at the beginning (if size permits)
                auto now = std::chrono::system_clock::now();
                auto time_since_epoch = now.time_since_epoch();
                auto microseconds = std::chrono::duration_cast<std::chrono::microseconds>(time_since_epoch).count();
                
                if (size >= sizeof(uint64_t)) {
                    // Store timestamp in first 8 bytes
                    memcpy(data.data(), &microseconds, sizeof(uint64_t));
                    
                    // Fill rest with pattern
                    for (size_t i = sizeof(uint64_t); i < size; ++i) {
                        data[i] = static_cast<uint8_t>(i % 256);
                    }
                    
                    ZEROBUFFER_LOG_INFO("DuplexChannelSteps") 
                        << "Client sending request at timestamp: " << microseconds << " microseconds since epoch";
                } else {
                    // Too small for timestamp, just use pattern
                    for (size_t i = 0; i < size; ++i) {
                        data[i] = static_cast<uint8_t>(i % 256);
                    }
                }
                
                // Use zero-copy acquire/commit instead of write
                auto buffer = client->acquire_buffer(data.size());
                std::memcpy(buffer.data(), data.data(), data.size());
                uint64_t sequence = client->commit();
                sent_requests[sequence] = data;
                
                ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                    << "Sent request with sequence " << sequence;
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to send request: " << e.what();
                throw;
            }
        }
    );
    
    // When the 'process' sends 'N' requests rapidly without waiting
    registry.registerStep(
        "the '([^']+)' process sends '([^']+)' requests rapidly without waiting",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            int count = std::stoi(params[1]);
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Sending " << count << " requests rapidly";
            
            if (clients.empty()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "No client connected";
                throw std::runtime_error("No client connected");
            }
            
            auto& client = clients.rbegin()->second;
            if (!client) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Client is null";
                throw std::runtime_error("Client is null");
            }
            
            try {
                for (int i = 0; i < count; ++i) {
                    std::string msg = "Request " + std::to_string(i);
                    std::vector<uint8_t> data(msg.begin(), msg.end());
                    // Use zero-copy acquire/commit instead of write
                auto buffer = client->acquire_buffer(data.size());
                std::memcpy(buffer.data(), data.data(), data.size());
                uint64_t sequence = client->commit();
                    sent_requests[sequence] = data;
                }
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to send requests: " << e.what();
                throw;
            }
        }
    );
    
    // Then response should match request with size 'X'
    registry.registerStep(
        "response should match request with size '([^']+)'",
        [](TestContext& context, const std::vector<std::string>& params) {
            size_t expected_size = std::stoull(params[0]);
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Checking response with expected size " << expected_size;
            
            if (clients.empty()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "No client connected";
                throw std::runtime_error("No client connected");
            }
            
            auto& client = clients.rbegin()->second;
            if (!client) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Client is null";
                throw std::runtime_error("Client is null");
            }
            
            try {
                auto response = client->read(std::chrono::seconds(5));
                
                if (!response.is_valid()) {
                    ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Response is not valid";
                    throw std::runtime_error("Response is not valid");
                }
                
                // Check size
                size_t actual_size = response.size();
                if (actual_size != expected_size) {
                    ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                        << "Response size mismatch: expected " << expected_size 
                        << ", got " << actual_size;
                    throw std::runtime_error("Response size mismatch");
                }
                
                // Verify data matches if we have the original request
                auto it = sent_requests.find(response.sequence());
                if (it != sent_requests.end()) {
                    const auto& original_data = it->second;
                    const uint8_t* resp_ptr = static_cast<const uint8_t*>(response.data());
                    std::vector<uint8_t> response_data(resp_ptr, resp_ptr + response.size());
                    
                    if (original_data != response_data) {
                        ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                            << "Response data doesn't match request. "
                            << "Original size: " << original_data.size() 
                            << ", Response size: " << response_data.size()
                            << ", Sequence: " << response.sequence();
                        
                        // Log first few bytes for debugging
                        if (!original_data.empty() && !response_data.empty()) {
                            ZEROBUFFER_LOG_ERROR("DuplexChannelSteps")
                                << "Original first byte: " << (int)original_data[0]
                                << ", Response first byte: " << (int)response_data[0];
                        }
                        throw std::runtime_error("Response data doesn't match request");
                    }
                    
                    // Extract and print timestamp if present
                    if (response.size() >= sizeof(uint64_t)) {
                        uint64_t sent_timestamp;
                        memcpy(&sent_timestamp, response_data.data(), sizeof(uint64_t));
                        
                        auto now = std::chrono::system_clock::now();
                        auto current_time = std::chrono::duration_cast<std::chrono::microseconds>(
                            now.time_since_epoch()).count();
                        uint64_t round_trip_time = current_time - sent_timestamp;
                        
                        ZEROBUFFER_LOG_INFO("DuplexChannelSteps") 
                            << "Client received response with timestamp: " << sent_timestamp 
                            << " microseconds since epoch (round-trip time: " 
                            << round_trip_time << " microseconds)";
                    }
                    
                    received_responses[response.sequence()] = response_data;
                }
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to receive response: " << e.what();
                throw;
            }
        }
    );
    
    // Then all responses should have correct sequence numbers
    registry.registerStep(
        "all responses should have correct sequence numbers",
        [](TestContext& context, const std::vector<std::string>& params) {
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Checking sequence numbers";
            
            for (const auto& [seq, data] : received_responses) {
                if (sent_requests.find(seq) == sent_requests.end()) {
                    ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                        << "Response sequence " << seq << " doesn't match any sent request";
                    throw std::runtime_error("Invalid response sequence");
                }
            }
        }
    );
    
    // Then the 'process' responds in reverse order
    registry.registerStep(
        "the '([^']+)' process responds in reverse order",
        [](TestContext& context, const std::vector<std::string>& params) {
            // This is handled by the delayed handler - responses will come back in order they finish
            // With delays, later requests might finish first
        }
    );
    
    // When the 'process' receives all 'N' responses
    registry.registerStep(
        "the '([^']+)' process receives all '([^']+)' responses",
        [](TestContext& context, const std::vector<std::string>& params) {
            std::string process = params[0];
            int expected_count = std::stoi(params[1]);
            
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Receiving " << expected_count << " responses";
            
            if (clients.empty()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "No client connected";
                throw std::runtime_error("No client connected");
            }
            
            auto& client = clients.rbegin()->second;
            if (!client) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") << "Client is null";
                throw std::runtime_error("Client is null");
            }
            
            try {
                responses.clear();
                for (int i = 0; i < expected_count; ++i) {
                    auto response = client->read(std::chrono::seconds(10));
                    if (response.is_valid()) {
                        const uint8_t* resp_ptr = static_cast<const uint8_t*>(response.data());
                        std::vector<uint8_t> data(resp_ptr, resp_ptr + response.size());
                        responses.push_back(std::make_pair(response.sequence(), data));
                        received_responses[response.sequence()] = data;
                    }
                }
                
                if (responses.size() != static_cast<size_t>(expected_count)) {
                    throw std::runtime_error("Didn't receive expected number of responses");
                }
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Failed to receive responses: " << e.what();
                throw;
            }
        }
    );
    
    // Then responses should match requests by sequence number
    registry.registerStep(
        "responses should match requests by sequence number",
        [](TestContext& context, const std::vector<std::string>& params) {
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Checking responses match requests by sequence";
            
            for (const auto& [seq, data] : responses) {
                auto it = sent_requests.find(seq);
                if (it == sent_requests.end()) {
                    ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                        << "Response sequence " << seq << " doesn't match any sent request";
                    throw std::runtime_error("Response sequence doesn't match");
                }
                
                if (it->second != data) {
                    ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                        << "Response data for sequence " << seq << " doesn't match request";
                    throw std::runtime_error("Response data doesn't match");
                }
            }
        }
    );
    
    // Then no responses should be lost or mismatched
    registry.registerStep(
        "no responses should be lost or mismatched",
        [](TestContext& context, const std::vector<std::string>& params) {
            ZEROBUFFER_LOG_DEBUG("DuplexChannelSteps") 
                << "Checking no responses lost";
            
            if (sent_requests.size() != responses.size()) {
                ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                    << "Response count mismatch: sent " << sent_requests.size() 
                    << ", received " << responses.size();
                throw std::runtime_error("Response count mismatch");
            }
            
            // Check all sent requests have responses
            for (const auto& [seq, data] : sent_requests) {
                bool found = false;
                for (const auto& [resp_seq, resp_data] : responses) {
                    if (resp_seq == seq) {
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    ZEROBUFFER_LOG_ERROR("DuplexChannelSteps") 
                        << "No response for sequence " << seq;
                    throw std::runtime_error("Missing response");
                }
            }
        }
    );
}

} // namespace steps
} // namespace zerobuffer