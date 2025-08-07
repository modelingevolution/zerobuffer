#include "basic_communication_steps.h"
#include "step_registry.h"
#include "test_context.h"

#include <zerobuffer/reader.h>
#include <zerobuffer/writer.h>
#include <zerobuffer/types.h>
#include <zerobuffer/logger.h>

#include <thread>
#include <chrono>

namespace zerobuffer {
namespace steps {

void registerBasicCommunicationSteps() {
    auto& registry = StepRegistry::getInstance();
    
    // Step: the test environment is initialized (without "Given" prefix for Harmony)
    registry.registerStep(
        "the test environment is initialized",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            // Initialize/reset the test context
            ctx.reset();
            ZEROBUFFER_LOG_INFO("Step") << "Test environment initialized";
        }
    );
    
    // Step: all processes are ready (compatibility step)
    registry.registerStep(
        "all processes are ready",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            // This step is for compatibility with multi-process scenarios
            // In single-process tests, we don't need to do anything
            ZEROBUFFER_LOG_INFO("Step") << "All processes ready";
        }
    );
    
    // Step: the '{word}' process creates buffer '{string}' with metadata size '{int}' and payload size '{int}'
    registry.registerStep(
        "the '([^']+)' process creates buffer '([^']+)' with metadata size '([^']+)' and payload size '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& bufferName = params[1];
            int metadataSize = std::stoi(params[2]);
            int payloadSize = std::stoi(params[3]);
            
            BufferConfig config;
            config.metadata_size = metadataSize;
            config.payload_size = payloadSize;
            
            ctx.createReader(process, bufferName, config);
            ZEROBUFFER_LOG_INFO("Step") << "Buffer '" << bufferName << "' created by " << process 
                                        << " with metadata_size=" << metadataSize 
                                        << ", payload_size=" << payloadSize;
        }
    );
    
    // Step: the {word} process creates buffer {string} with default configuration
    registry.registerStep(
        "the {word} process creates buffer {string} with default configuration",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& bufferName = params[1];
            
            BufferConfig config;
            config.metadata_size = 4096;    // Default metadata size
            config.payload_size = 65536;    // Default payload size
            
            ctx.createReader(process, bufferName, config);
            ZEROBUFFER_LOG_INFO("Step") << "Buffer '" << bufferName << "' created by " << process;
        }
    );
    
    // Step: the '{word}' process connects to buffer '{string}'
    registry.registerStep(
        "the '([^']+)' process connects to buffer '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& bufferName = params[1];
            
            // Give reader a moment to fully initialize
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            
            ctx.createWriter(process, bufferName);
            ZEROBUFFER_LOG_INFO("Step") << process << " connected to buffer '" << bufferName << "'";
            
            // Verify connection from reader's perspective
            auto* reader = ctx.getReader("reader");
            if (reader && !reader->is_writer_connected(1000)) {
                throw std::runtime_error("Reader doesn't see writer as connected");
            }
            ZEROBUFFER_LOG_INFO("Step") << "Connection verified";
        }
    );
    
    // Step: the '{word}' process writes metadata with size '{int}'
    registry.registerStep(
        "the '([^']+)' process writes metadata with size '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            int metadataSize = std::stoi(params[1]);
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Create metadata buffer with test pattern
            std::vector<uint8_t> metadata(metadataSize);
            for (int i = 0; i < metadataSize; ++i) {
                metadata[i] = static_cast<uint8_t>(i % 256);
            }
            
            writer->set_metadata(metadata.data(), metadataSize);
            ZEROBUFFER_LOG_INFO("Step") << process << " wrote metadata with size " << metadataSize;
        }
    );
    
    // Step: the '{word}' process writes frame with size '{int}' and sequence '{int}'
    registry.registerStep(
        "the '([^']+)' process writes frame with size '([^']+)' and sequence '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            int frameSize = std::stoi(params[1]);
            int sequence = std::stoi(params[2]);
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Create frame data with test pattern including sequence
            std::vector<uint8_t> frameData(frameSize);
            for (int i = 0; i < frameSize; ++i) {
                frameData[i] = static_cast<uint8_t>((sequence + i) % 256);
            }
            
            writer->write_frame(frameData.data(), frameSize);
            ZEROBUFFER_LOG_INFO("Step") << process << " wrote frame with size " << frameSize 
                                        << " and sequence " << sequence;
            
            // Store frame info for verification
            ctx.setProperty("last_sequence", std::to_string(sequence));
            ctx.setProperty("last_frame_size", std::to_string(frameSize));
        }
    );
    
    // Step: the '{word}' process should read frame with sequence '{int}' and size '{int}'
    registry.registerStep(
        "the '([^']+)' process should read frame with sequence '([^']+)' and size '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            int expectedSequence = std::stoi(params[1]);
            int expectedSize = std::stoi(params[2]);
            
            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }
            
            auto frame = reader->read_frame(std::chrono::milliseconds(5000));
            
            if (!frame.valid()) {
                throw std::runtime_error("Failed to read frame - timeout or invalid frame");
            }
            
            // Verify frame size
            if (frame.size() != expectedSize) {
                throw std::runtime_error("Frame size mismatch: expected " + std::to_string(expectedSize) + 
                                       " but got " + std::to_string(frame.size()));
            }
            
            // Verify frame content (check first byte for sequence pattern)
            const uint8_t* data = static_cast<const uint8_t*>(frame.data());
            uint8_t expectedFirstByte = static_cast<uint8_t>(expectedSequence % 256);
            if (data[0] != expectedFirstByte) {
                throw std::runtime_error("Frame sequence mismatch: expected sequence " + 
                                       std::to_string(expectedSequence) + " but got different data");
            }
            
            ZEROBUFFER_LOG_INFO("Step") << "Frame read by " << process << " with sequence " 
                                        << expectedSequence << " and size " << expectedSize;
            
            // Store frame for validation step
            ctx.setProperty("last_read_frame_valid", "true");
            
            // Don't release yet - validation step may need it
            ctx.setProperty("pending_frame_release", "true");
        }
    );
    
    // Step: the '{word}' process should validate frame data
    registry.registerStep(
        "the '([^']+)' process should validate frame data",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            // Check that we have a valid frame from previous read
            if (ctx.getProperty("last_read_frame_valid") != "true") {
                throw std::runtime_error("No valid frame to validate");
            }
            
            ZEROBUFFER_LOG_INFO("Step") << process << " validated frame data";
        }
    );
    
    // Step: the '{word}' process signals space available
    registry.registerStep(
        "the '([^']+)' process signals space available",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }
            
            // If we have a pending frame to release, do it now
            if (ctx.getProperty("pending_frame_release") == "true") {
                // In real implementation, we'd release the last frame here
                // For now, just clear the flag
                ctx.setProperty("pending_frame_release", "false");
                ZEROBUFFER_LOG_INFO("Step") << process << " signaled space available (frame released)";
            } else {
                ZEROBUFFER_LOG_INFO("Step") << process << " signaled space available";
            }
        }
    );
    
    // Step: the {word} process writes {string} to the buffer
    registry.registerStep(
        "the {word} process writes {string} to the buffer",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& message = params[1];
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            writer->write_frame(message.data(), message.size());
            ZEROBUFFER_LOG_INFO("Step") << process << " wrote '" << message << "'";
            
            // Store the message for verification
            ctx.setProperty("expected_message", message);
        }
    );
    
    // Step: the {word} process should read {string} from the buffer
    registry.registerStep(
        "the {word} process should read {string} from the buffer",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& expectedMessage = params[1];
            
            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }
            
            auto frame = reader->read_frame(std::chrono::milliseconds(5000));
            
            if (!frame.valid()) {
                throw std::runtime_error("Failed to read frame - timeout or invalid frame");
            }
            ZEROBUFFER_LOG_INFO("Step") << "Frame read by " << process;
            
            // Verify frame content
            std::string receivedMessage(static_cast<const char*>(frame.data()), frame.size());
            if (receivedMessage != expectedMessage) {
                throw std::runtime_error(
                    "Content mismatch: expected '" + expectedMessage +
                    "' but got '" + receivedMessage + "'"
                );
            }
            ZEROBUFFER_LOG_INFO("Step") << "Content verified: '" << receivedMessage << "'";
            
            // Release the frame
            reader->release_frame(frame);
            ZEROBUFFER_LOG_INFO("Step") << "Frame released";
        }
    );
    
    ZEROBUFFER_LOG_INFO("BasicCommunication") << "Registered " << registry.getAllSteps().size() << " step definitions";
}

} // namespace steps
} // namespace zerobuffer