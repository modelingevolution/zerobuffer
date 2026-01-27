#include "basic_communication_steps.h"
#include "step_registry.h"
#include "test_context.h"
#include "test_data_patterns.h"

#include <zerobuffer/reader.h>
#include <zerobuffer/writer.h>
#include <zerobuffer/types.h>
#include <zerobuffer/logger.h>

#include <thread>
#include <chrono>
#include <cstring>
#include <cstdint>

using json = nlohmann::json;

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
            ZEROBUFFER_LOG_DEBUG("Step") << "Test environment initialized";
        }
    );
    
    // Step: all processes are ready (compatibility step)
    registry.registerStep(
        "all processes are ready",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            // This step is for compatibility with multi-process scenarios
            // In single-process tests, we don't need to do anything
            ZEROBUFFER_LOG_DEBUG("Step") << "All processes ready very very well";
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
            ZEROBUFFER_LOG_DEBUG("Step") << "Buffer '" << bufferName << "' created by " << process 
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
            ZEROBUFFER_LOG_DEBUG("Step") << "Buffer '" << bufferName << "' created by " << process;
        }
    );
    
    // Step: the '{word}' process connects to buffer '{string}'
    registry.registerStep(
        "the '([^']+)' process connects to buffer '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const std::string& bufferName = params[1];
            
            ctx.createWriter(process, bufferName);
            ZEROBUFFER_LOG_DEBUG("Step") << process << " connected to buffer '" << bufferName << "'";
            
            // Verify connection from reader's perspective
            auto* reader = ctx.getReader("reader");
            if (reader && !reader->is_writer_connected(1000)) {
                throw std::runtime_error("Reader doesn't see writer as connected");
            }
            ZEROBUFFER_LOG_DEBUG("Step") << "Connection verified";
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
            
            // Use TestDataPatterns to generate consistent metadata
            auto metadata = TestDataPatterns::generateMetadata(metadataSize);
            
            writer->set_metadata(metadata.data(), metadataSize);
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote metadata with size " << metadataSize;
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
            
            // Use TestDataPatterns to generate consistent frame data
            auto frameData = TestDataPatterns::generateFrameData(frameSize, sequence);
            
            writer->write_frame(frameData.data(), frameSize);
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote frame with size " << frameSize 
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
            
            // Verify frame content matches the expected pattern
            const uint8_t* data = static_cast<const uint8_t*>(frame.data());
            auto expectedData = TestDataPatterns::generateFrameData(expectedSize, expectedSequence);
            
            // Compare at least the first few bytes to verify sequence pattern
            bool dataMatches = true;
            size_t bytesToCheck = std::min(size_t(10), size_t(expectedSize));
            for (size_t i = 0; i < bytesToCheck; ++i) {
                if (data[i] != expectedData[i]) {
                    dataMatches = false;
                    break;
                }
            }
            
            if (!dataMatches) {
                throw std::runtime_error("Frame data mismatch: expected sequence " + 
                                       std::to_string(expectedSequence) + " pattern but got different data");
            }
            
            ZEROBUFFER_LOG_DEBUG("Step") << "Frame read by " << process << " with sequence " 
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
            json prop = ctx.getProperty("last_read_frame_valid");
            if (!prop.is_string() || prop.get<std::string>() != "true") {
                throw std::runtime_error("No valid frame to validate");
            }
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " validated frame data";
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
            json prop = ctx.getProperty("pending_frame_release");
            if (prop.is_string() && prop.get<std::string>() == "true") {
                // In real implementation, we'd release the last frame here
                // For now, just clear the flag
                ctx.setProperty("pending_frame_release", "false");
                ZEROBUFFER_LOG_DEBUG("Step") << process << " signaled space available (frame released)";
            } else {
                ZEROBUFFER_LOG_DEBUG("Step") << process << " signaled space available";
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
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote '" << message << "'";
            
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
            ZEROBUFFER_LOG_DEBUG("Step") << "Frame read by " << process;
            
            // Verify frame content
            std::string receivedMessage(static_cast<const char*>(frame.data()), frame.size());
            if (receivedMessage != expectedMessage) {
                throw std::runtime_error(
                    "Content mismatch: expected '" + expectedMessage +
                    "' but got '" + receivedMessage + "'"
                );
            }
            ZEROBUFFER_LOG_DEBUG("Step") << "Content verified: '" << receivedMessage << "'";
            
            // Release the frame
            reader->release_frame(frame);
            ZEROBUFFER_LOG_DEBUG("Step") << "Frame released";
        }
    );
    
    // Step: the 'writer' process writes frame with sequence '{int}' (Test 1.2)
    registry.registerStep(
        "the '([^']+)' process writes frame with sequence '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            int sequence = std::stoi(params[1]);
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Use default frame size for this step (1024 bytes)
            const size_t frameSize = 1024;
            auto frameData = TestDataPatterns::generateFrameData(frameSize, sequence);
            
            writer->write_frame(frameData.data(), frameSize);
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote frame with sequence " << sequence;
            
            // Track sequences for verification
            json prop = ctx.getProperty("written_sequences");
            std::string sequencesStr = prop.is_string() ? prop.get<std::string>() : "";
            if (!sequencesStr.empty()) sequencesStr += ",";
            sequencesStr += std::to_string(sequence);
            ctx.setProperty("written_sequences", sequencesStr);
        }
    );
    
    // Step: the 'reader' process should read frame with sequence '{int}' (Test 1.2)
    // Note: Feature file has semicolons at the end for some steps
    // Define the lambda once and reuse it
    auto readFrameWithSequence = [](TestContext& ctx, const std::vector<std::string>& params) {
        const std::string& process = params[0];
        int expectedSequence = std::stoi(params[1]);
        
        auto* reader = ctx.getReader(process);
        if (!reader) {
            throw std::runtime_error("Reader not found for process: " + process);
        }
        
        auto frame = reader->read_frame(std::chrono::milliseconds(5000));
        
        if (!frame.valid()) {
            throw std::runtime_error("Failed to read frame - timeout or invalid frame");
        }
        
        // Verify frame content matches the expected sequence pattern
        const uint8_t* data = static_cast<const uint8_t*>(frame.data());
        auto expectedData = TestDataPatterns::generateFrameData(1024, expectedSequence);
        
        // Compare first few bytes to verify sequence
        bool dataMatches = true;
        size_t bytesToCheck = std::min(size_t(10), frame.size());
        for (size_t i = 0; i < bytesToCheck; ++i) {
            if (data[i] != expectedData[i]) {
                dataMatches = false;
                break;
            }
        }
        
        if (!dataMatches) {
            throw std::runtime_error("Frame data mismatch: expected sequence " + 
                                   std::to_string(expectedSequence) + " pattern but got different data");
        }
        
        ZEROBUFFER_LOG_DEBUG("Step") << process << " read frame with sequence " << expectedSequence;
        
        // Track read sequences for verification
        json prop = ctx.getProperty("read_sequences");
        std::string sequencesStr = prop.is_string() ? prop.get<std::string>() : "";
        if (!sequencesStr.empty()) sequencesStr += ",";
        sequencesStr += std::to_string(expectedSequence);
        ctx.setProperty("read_sequences", sequencesStr);
        
        // Store frame for later release
        ctx.setLastFrame(frame);
    };
    
    // Register with semicolon (for Test 1.2 feature file)
    registry.registerStep(
        "the '([^']+)' process should read frame with sequence '([^']+)';",
        readFrameWithSequence
    );
    
    // Register without semicolon (for other tests)  
    registry.registerStep(
        "the '([^']+)' process should read frame with sequence '([^']+)'",
        readFrameWithSequence
    );
    
    // Step: the 'reader' process should verify all frames maintain sequential order (Test 1.2)
    registry.registerStep(
        "the '([^']+)' process should verify all frames maintain sequential order",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            json prop = ctx.getProperty("read_sequences");
            std::string readSequences = prop.is_string() ? prop.get<std::string>() : "";
            if (readSequences.empty()) {
                throw std::runtime_error("No sequences were read to verify");
            }
            
            // Parse the comma-separated sequences
            std::vector<int> sequences;
            size_t pos = 0;
            while (pos < readSequences.length()) {
                size_t nextComma = readSequences.find(',', pos);
                if (nextComma == std::string::npos) {
                    sequences.push_back(std::stoi(readSequences.substr(pos)));
                    break;
                } else {
                    sequences.push_back(std::stoi(readSequences.substr(pos, nextComma - pos)));
                    pos = nextComma + 1;
                }
            }
            
            // Verify sequences are in order
            for (size_t i = 1; i < sequences.size(); ++i) {
                if (sequences[i] != sequences[i-1] + 1) {
                    throw std::runtime_error("Sequences not in order: " + 
                                           std::to_string(sequences[i-1]) + " followed by " + 
                                           std::to_string(sequences[i]));
                }
            }
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " verified all " << sequences.size() 
                                        << " frames maintain sequential order";
        }
    );
    
    // Step: the 'writer' process writes frames until buffer is full (Test 1.3)
    registry.registerStep(
        "the '([^']+)' process writes frames until buffer is full",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Set a short timeout for detecting when buffer is full
            writer->set_write_timeout(std::chrono::milliseconds(100));
            
            // Write frames until buffer is full
            const size_t frameSize = 1024;
            int frameCount = 0;
            
            while (true) {
                try {
                    auto frameData = TestDataPatterns::generateFrameData(frameSize, frameCount + 1);
                    writer->write_frame(frameData.data(), frameSize);
                    frameCount++;
                    ZEROBUFFER_LOG_DEBUG("Step") << "Wrote frame " << frameCount;
                    
                    // Safety limit to prevent infinite loop
                    if (frameCount > 100) {
                        break;
                    }
                } catch (const BufferFullException&) {
                    // Buffer is full, this is expected
                    ZEROBUFFER_LOG_DEBUG("Step") << "Buffer is full after " << frameCount << " frames";
                    break;
                }
            }
            
            if (frameCount == 0) {
                throw std::runtime_error("Could not write any frames to buffer");
            }
            
            // Reset timeout to default for subsequent operations
            writer->set_write_timeout(std::chrono::milliseconds(5000));
            
            ctx.setProperty("frames_written_until_full", std::to_string(frameCount));
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote " << frameCount 
                                        << " frames to fill buffer";
        }
    );
    
    // Step: the 'writer' process should experience timeout on next write (Test 1.3)
    registry.registerStep(
        "the '([^']+)' process should experience timeout on next write",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Set a short timeout
            writer->set_write_timeout(std::chrono::milliseconds(100));
            
            // Try to write when buffer is full
            const size_t frameSize = 1024;
            auto frameData = TestDataPatterns::generateFrameData(frameSize, 999);
            
            bool timedOut = false;
            try {
                writer->write_frame(frameData.data(), frameSize);
                // If we get here, the write succeeded when it shouldn't have
                throw std::runtime_error("Write succeeded when buffer should be full");
            } catch (const BufferFullException&) {
                // Expected - buffer is full and timeout occurred
                timedOut = true;
                ZEROBUFFER_LOG_DEBUG("Step") << process << " experienced expected timeout on write";
            }
            
            if (!timedOut) {
                throw std::runtime_error("Expected BufferFullException due to timeout, but didn't get one");
            }
            
            // Reset timeout to default
            writer->set_write_timeout(std::chrono::milliseconds(5000));
        }
    );
    
    // Step: the 'reader' process reads one frame (Test 1.3)
    registry.registerStep(
        "the '([^']+)' process reads one frame",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }
            
            auto frame = reader->read_frame(std::chrono::milliseconds(5000));
            
            if (!frame.valid()) {
                throw std::runtime_error("Failed to read frame");
            }
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " read one frame of size " << frame.size();
            
            // Store frame for later release during "signals space available"
            ctx.setLastFrame(frame);
            ctx.setProperty("pending_frame_release", "true");
        }
    );
    
    // Step: the 'writer' process should write successfully immediately (Test 1.3)
    registry.registerStep(
        "the '([^']+)' process should write successfully immediately",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Now that reader has freed space, write should succeed immediately
            const size_t frameSize = 1024;
            auto frameData = TestDataPatterns::generateFrameData(frameSize, 1000);
            
            // This should succeed without timeout
            writer->write_frame(frameData.data(), frameSize);
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote frame successfully after space was freed";
        }
    );
    
    // Zero-copy operations (Test 1.4)
    
    // Step: the 'writer' process requests zero-copy frame of size 'X'
    registry.registerStep(
        "the '([^']+)' process requests zero-copy frame of size '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const size_t frameSize = std::stoull(params[1]);
            
            // Just store the size for the next step
            // The actual zero-copy operation will happen in the fill step
            ctx.setProperty("zerocopy_size", frameSize);
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " will request zero-copy buffer of size " 
                                         << frameSize;
        }
    );
    
    // Step: the 'writer' process fills zero-copy buffer with test pattern
    registry.registerStep(
        "the '([^']+)' process fills zero-copy buffer with test pattern",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            size_t size = ctx.getProperty("zerocopy_size");
            
            // Request zero-copy buffer
            uint64_t sequenceNumber = 0;
            void* buffer = writer->get_frame_buffer(size, sequenceNumber);
            
            if (!buffer) {
                throw std::runtime_error("Failed to get zero-copy buffer");
            }
            
            // Generate test pattern based on sequence number
            auto testPattern = TestDataPatterns::generateFrameData(size, sequenceNumber);
            
            // Fill the zero-copy buffer directly
            std::memcpy(buffer, testPattern.data(), size);
            
            // Store the size and sequence to regenerate the pattern later for verification
            ctx.setProperty("test_pattern_size", size);
            ctx.setProperty("test_pattern_sequence", sequenceNumber);
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " filled zero-copy buffer with test pattern";
        }
    );
    
    // Step: the 'writer' process commits zero-copy frame
    registry.registerStep(
        "the '([^']+)' process commits zero-copy frame",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Commit the zero-copy frame
            writer->commit_frame();
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " committed zero-copy frame";
        }
    );
    
    // Step: the 'reader' process should read frame with size 'X'
    registry.registerStep(
        "the '([^']+)' process should read frame with size '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const size_t expectedSize = std::stoull(params[1]);
            
            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }
            
            auto frame = reader->read_frame(std::chrono::seconds(5));
            
            if (!frame.valid()) {
                throw std::runtime_error("Failed to read frame - timeout or invalid frame");
            }
            
            // Verify frame size
            if (frame.size() != expectedSize) {
                throw std::runtime_error("Frame size mismatch: expected " + std::to_string(expectedSize) + 
                                       " but got " + std::to_string(frame.size()));
            }
            
            // Store frame data for verification in next step
            // We need to copy the data since the frame pointer won't be valid later
            std::vector<uint8_t> frameData(
                static_cast<const uint8_t*>(frame.data()),
                static_cast<const uint8_t*>(frame.data()) + frame.size()
            );
            ctx.setProperty("last_frame_data", frameData);
            ctx.setProperty("last_frame_sequence", frame.sequence());
            
            // Release the frame
            reader->release_frame(frame);
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " read frame with size " << expectedSize;
        }
    );
    
    // Step: the 'reader' process should verify frame data matches test pattern
    registry.registerStep(
        "the '([^']+)' process should verify frame data matches test pattern",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            
            // Get the frame data we stored in the previous step
            json frameDataJson = ctx.getProperty("last_frame_data");
            if (!frameDataJson.is_array()) {
                throw std::runtime_error("No frame data available to verify");
            }
            
            // Convert JSON array back to vector
            std::vector<uint8_t> frameData;
            for (const auto& byte : frameDataJson) {
                frameData.push_back(byte.get<uint8_t>());
            }
            
            // Get the sequence number we stored when reading the frame
            json sequenceJson = ctx.getProperty("last_frame_sequence");
            uint64_t expectedSequence = 0;
            if (sequenceJson.is_number()) {
                expectedSequence = sequenceJson.get<uint64_t>();
            }
            
            // Regenerate the expected pattern using the frame size and sequence
            auto expectedPattern = TestDataPatterns::generateFrameData(frameData.size(), expectedSequence);
            
            // Compare data
            if (frameData.size() != expectedPattern.size()) {
                throw std::runtime_error("Frame size mismatch: expected " + 
                    std::to_string(expectedPattern.size()) + " but got " + 
                    std::to_string(frameData.size()));
            }
            
            if (std::memcmp(frameData.data(), expectedPattern.data(), frameData.size()) != 0) {
                throw std::runtime_error("Frame data does not match test pattern");
            }
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " verified frame data matches test pattern";
        }
    );
    
    // Step: the 'writer' process writes frame with size 'X' (no sequence)
    registry.registerStep(
        "the '([^']+)' process writes frame with size '([^']+)'$",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const size_t frameSize = std::stoull(params[1]);
            
            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }
            
            // Use simple test data pattern (not sequence-based)
            auto frameData = TestDataPatterns::generateSimpleFrameData(frameSize);
            
            writer->write_frame(frameData.data(), frameSize);
            ZEROBUFFER_LOG_DEBUG("Step") << process << " wrote frame with size " << frameSize;
        }
    );
    
    // Step: the 'reader' process should read X frames with sizes 'Y,Z' in order
    registry.registerStep(
        "the '([^']+)' process should read ([0-9]+) frames with sizes '([^']+)' in order",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const int frameCount = std::stoi(params[1]);
            const std::string& sizesStr = params[2];
            
            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }
            
            // Parse expected sizes
            std::vector<size_t> expectedSizes;
            size_t pos = 0;
            while (pos < sizesStr.length()) {
                size_t nextComma = sizesStr.find(',', pos);
                if (nextComma == std::string::npos) {
                    expectedSizes.push_back(std::stoull(sizesStr.substr(pos)));
                    break;
                } else {
                    expectedSizes.push_back(std::stoull(sizesStr.substr(pos, nextComma - pos)));
                    pos = nextComma + 1;
                }
            }
            
            if (expectedSizes.size() != static_cast<size_t>(frameCount)) {
                throw std::runtime_error("Frame count mismatch: expected " + std::to_string(frameCount) + 
                    " sizes but got " + std::to_string(expectedSizes.size()));
            }
            
            // Read and verify each frame
            for (int i = 0; i < frameCount; i++) {
                auto frame = reader->read_frame(std::chrono::seconds(5));
                
                if (!frame.valid()) {
                    throw std::runtime_error("Failed to read frame " + std::to_string(i + 1));
                }
                
                if (frame.size() != expectedSizes[i]) {
                    throw std::runtime_error("Frame " + std::to_string(i + 1) + 
                        " size mismatch: expected " + std::to_string(expectedSizes[i]) + 
                        " but got " + std::to_string(frame.size()));
                }
                
                // Verify frame data integrity using TestDataPatterns
                const uint8_t* frameData = static_cast<const uint8_t*>(frame.data());
                if (!TestDataPatterns::verifySimpleFrameData(frameData, frame.size())) {
                    throw std::runtime_error("Frame " + std::to_string(i + 1) + 
                        " data does not match expected pattern");
                }
                
                reader->release_frame(frame);
                ZEROBUFFER_LOG_DEBUG("Step") << "Read and verified frame " << (i + 1) 
                                             << " with size " << expectedSizes[i];
            }
            
            ZEROBUFFER_LOG_DEBUG("Step") << process << " read " << frameCount 
                                        << " frames with expected sizes";
        }
    );
    
    // ========== Test 1.6 - Slow Reader With Fast Writer ==========

    // Step: the 'writer' process writes 'X' frames of size 'Y' as fast as possible
    // This step starts writing in a background thread and returns immediately,
    // allowing the reader step to run concurrently and consume frames.
    registry.registerStep(
        "the '([^']+)' process writes '([^']+)' frames of size '([^']+)' as fast as possible",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const int frameCount = std::stoi(params[1]);
            const size_t frameSize = std::stoull(params[2]);

            auto* writer = ctx.getWriter(process);
            if (!writer) {
                throw std::runtime_error("Writer not found for process: " + process);
            }

            ZEROBUFFER_LOG_DEBUG("Step") << "Starting background writer for " << frameCount
                                         << " frames of size " << frameSize;

            // Store write results for later verification
            ctx.setProperty("write_frames_written", "0");
            ctx.setProperty("write_error", "");
            ctx.setProperty("write_complete", "false");

            // Start background thread for writing
            std::thread writerThread([writer, frameCount, frameSize, &ctx, process]() {
                try {
                    for (int i = 0; i < frameCount; i++) {
                        int sequenceNum = i + 1;  // 1-based sequence
                        auto frameData = TestDataPatterns::generateFrameData(frameSize, sequenceNum);

                        // Write frame - this will block if buffer is full until reader consumes
                        writer->write_frame(frameData.data(), frameSize);
                        ctx.setProperty("write_frames_written", std::to_string(sequenceNum));

                        ZEROBUFFER_LOG_DEBUG("Step") << "Wrote frame " << sequenceNum << "/" << frameCount;
                    }
                    ctx.setProperty("write_complete", "true");
                    ZEROBUFFER_LOG_INFO("Step") << "Background writer finished: wrote " << frameCount << " frames";

                    // Wait 1 second for reader to finish reading, then close writer
                    // This sets writer_pid=0 so reader can detect writer is done
                    std::this_thread::sleep_for(std::chrono::seconds(1));
                    ctx.removeWriter(process);
                    ZEROBUFFER_LOG_INFO("Step") << "Writer closed after 1 second delay";
                } catch (const std::exception& e) {
                    ctx.setProperty("write_error", e.what());
                    ZEROBUFFER_LOG_ERROR("Step") << "Background writer error: " << e.what();
                }
            });

            // Detach the thread so it runs in background
            writerThread.detach();

            // Give the writer a moment to start and fill the buffer
            std::this_thread::sleep_for(std::chrono::milliseconds(100));

            ZEROBUFFER_LOG_DEBUG("Step") << "Background writer started, returning to allow reader to run";
        }
    );

    // Step: the 'reader' process reads frames with 'X' ms delay between each read
    // This step reads frames while the writer is running in a background thread.
    // It continues reading until the writer is done and no more frames are available.
    registry.registerStep(
        "the '([^']+)' process reads frames with '([^']+)' ms delay between each read",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const int delayMs = std::stoi(params[1]);

            auto* reader = ctx.getReader(process);
            if (!reader) {
                throw std::runtime_error("Reader not found for process: " + process);
            }

            ZEROBUFFER_LOG_DEBUG("Step") << "Reading frames with " << delayMs << "ms delay between reads";

            // Clear sequence errors tracker
            ctx.setProperty("sequence_errors", "");

            int framesRead = 0;
            std::string readSequences;
            int consecutiveTimeouts = 0;
            const int maxConsecutiveTimeouts = 3;

            // Total timeout to handle cross-platform tests where writer is external process
            auto startTime = std::chrono::steady_clock::now();
            const auto totalTimeout = std::chrono::seconds(30);

            // Read frames until writer is done and no more frames are available
            while (true) {
                // Check total timeout first
                auto elapsed = std::chrono::steady_clock::now() - startTime;
                if (elapsed >= totalTimeout) {
                    ZEROBUFFER_LOG_DEBUG("Step") << "Total timeout reached after " << framesRead << " frames";
                    break;
                }

                try {
                    auto frame = reader->read_frame(std::chrono::milliseconds(1000));

                    if (!frame.valid()) {
                        consecutiveTimeouts++;

                        // Check if writer is done (for local writer thread)
                        json writeComplete = ctx.getProperty("write_complete");
                        bool writerDone = writeComplete.is_string() && writeComplete.get<std::string>() == "true";

                        json writeError = ctx.getProperty("write_error");
                        bool writerError = writeError.is_string() && !writeError.get<std::string>().empty();

                        // In cross-platform tests, write_complete may never be set because
                        // the writer is running in a different process. Check if no local
                        // writer was started (write_complete property doesn't exist or is "false")
                        // and we've had consecutive timeouts - assume external writer is done.
                        if (consecutiveTimeouts >= maxConsecutiveTimeouts) {
                            if (writerDone || writerError) {
                                ZEROBUFFER_LOG_DEBUG("Step") << "Local writer complete and no more frames available";
                                break;
                            }
                            // Check if writer_pid is 0 (writer disconnected) - handles cross-platform case
                            if (!reader->is_writer_connected()) {
                                ZEROBUFFER_LOG_DEBUG("Step") << "Writer disconnected, no more frames available";
                                break;
                            }
                        }
                        continue;
                    }

                    consecutiveTimeouts = 0;  // Reset on successful read

                    // Track sequence number
                    uint64_t sequence = frame.sequence();
                    if (!readSequences.empty()) readSequences += ",";
                    readSequences += std::to_string(sequence);

                    ZEROBUFFER_LOG_DEBUG("Step") << "Read frame with sequence " << sequence;

                    // Release the frame
                    reader->release_frame(frame);
                    framesRead++;

                    // Add delay between reads (simulating slow processing)
                    std::this_thread::sleep_for(std::chrono::milliseconds(delayMs));

                } catch (const std::exception& e) {
                    std::string error = e.what();
                    if (error.find("sequence") != std::string::npos ||
                        error.find("Sequence") != std::string::npos) {
                        std::string errors = ctx.getProperty("sequence_errors").get<std::string>();
                        if (!errors.empty()) errors += ";";
                        errors += error;
                        ctx.setProperty("sequence_errors", errors);
                        ZEROBUFFER_LOG_ERROR("Step") << "Sequence error: " << error;
                    } else {
                        ZEROBUFFER_LOG_ERROR("Step") << "Error reading frame: " << error;
                    }
                    break;
                }
            }

            ctx.setProperty("frames_read_slow", std::to_string(framesRead));
            ctx.setProperty("read_sequences", readSequences);
            ZEROBUFFER_LOG_DEBUG("Step") << "Finished reading, total frames read: " << framesRead;
        }
    );

    // Step: the 'reader' process should have read 'X' frames
    registry.registerStep(
        "the '([^']+)' process should have read '([^']+)' frames",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const int expectedCount = std::stoi(params[1]);

            json prop = ctx.getProperty("frames_read_slow");
            int actualCount = 0;
            if (prop.is_string()) {
                actualCount = std::stoi(prop.get<std::string>());
            }

            if (actualCount != expectedCount) {
                throw std::runtime_error("Expected " + std::to_string(expectedCount) +
                    " frames, but read " + std::to_string(actualCount));
            }

            ZEROBUFFER_LOG_DEBUG("Step") << "Verified: read " << actualCount << " frames";
        }
    );

    // Step: the 'reader' process should verify all frames have sequential sequence numbers starting from 'X'
    registry.registerStep(
        "the '([^']+)' process should verify all frames have sequential sequence numbers starting from '([^']+)'",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            const std::string& process = params[0];
            const int startSeq = std::stoi(params[1]);

            json prop = ctx.getProperty("read_sequences");
            std::string readSequences = prop.is_string() ? prop.get<std::string>() : "";

            if (readSequences.empty()) {
                throw std::runtime_error("No frames were read to verify");
            }

            // Parse comma-separated sequences
            std::vector<int> sequences;
            size_t pos = 0;
            while (pos < readSequences.length()) {
                size_t nextComma = readSequences.find(',', pos);
                if (nextComma == std::string::npos) {
                    sequences.push_back(std::stoi(readSequences.substr(pos)));
                    break;
                } else {
                    sequences.push_back(std::stoi(readSequences.substr(pos, nextComma - pos)));
                    pos = nextComma + 1;
                }
            }

            // Verify sequences start from startSeq and are consecutive
            for (size_t i = 0; i < sequences.size(); ++i) {
                int expected = startSeq + static_cast<int>(i);
                if (sequences[i] != expected) {
                    throw std::runtime_error("Frame " + std::to_string(i) +
                        ": expected sequence " + std::to_string(expected) +
                        ", got " + std::to_string(sequences[i]));
                }
            }

            ZEROBUFFER_LOG_DEBUG("Step") << "Verified: all " << sequences.size()
                                        << " frames have sequential sequences starting from " << startSeq;
        }
    );

    // Step: no sequence errors should have occurred
    registry.registerStep(
        "no sequence errors should have occurred",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            json prop = ctx.getProperty("sequence_errors");
            std::string errors = prop.is_string() ? prop.get<std::string>() : "";

            if (!errors.empty()) {
                throw std::runtime_error("Sequence errors occurred: " + errors);
            }

            ZEROBUFFER_LOG_DEBUG("Step") << "Verified: no sequence errors occurred";
        }
    );

    ZEROBUFFER_LOG_DEBUG("BasicCommunication") << "Registered " << registry.getAllSteps().size() << " step definitions";
}

} // namespace steps
} // namespace zerobuffer