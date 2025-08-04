#include <iostream>
#include <vector>
#include <string>
#include <chrono>
#include <cstring>
#include <getopt.h>
#include <random>
#include <iomanip>
#include <sstream>
#include <nlohmann/json.hpp>
#include <zerobuffer/zerobuffer.h>
#include <zerobuffer/logger.h>
#include <climits>

using json = nlohmann::json;

// Function to verify frame data matches expected pattern
bool verify_frame_data(const std::vector<uint8_t>& data, int frame_index, const std::string& pattern) {
    if (pattern == "sequential") {
        for (size_t i = 0; i < data.size(); ++i) {
            uint8_t expected = (frame_index + i) % 256;
            if (data[i] != expected) {
                return false;
            }
        }
        return true;
    } else if (pattern == "random") {
        std::mt19937 gen(frame_index);
        std::uniform_int_distribution<> dis(0, 255);
        for (size_t i = 0; i < data.size(); ++i) {
            uint8_t expected = dis(gen);
            if (data[i] != expected) {
                return false;
            }
        }
        return true;
    } else if (pattern == "zero") {
        return std::all_of(data.begin(), data.end(), [](uint8_t b) { return b == 0; });
    } else if (pattern == "ones") {
        return std::all_of(data.begin(), data.end(), [](uint8_t b) { return b == 0xFF; });
    } else if (pattern == "none") {
        return true; // No verification
    }
    throw std::invalid_argument("Unknown pattern: " + pattern);
}

// Calculate MD5 checksum (simplified - in production use a proper crypto library)
std::string calculate_checksum(const std::vector<uint8_t>& data) {
    // For now, just return a simple hash representation
    // In production, use OpenSSL or similar for proper MD5
    size_t hash = 0;
    for (uint8_t byte : data) {
        hash = hash * 31 + byte;
    }
    std::stringstream ss;
    ss << std::hex << hash;
    return ss.str();
}

void print_usage(const char* program_name) {
    std::cout << "Usage: " << program_name << " <buffer-name> [options]\n"
              << "\nOptions:\n"
              << "  -n, --frames NUM         Number of frames to read (default: 1000, -1 for unlimited)\n"
              << "  -s, --size SIZE          Expected size of each frame in bytes (default: 1024)\n"
              << "  --timeout-ms MS          Timeout for frame reads in milliseconds (default: 5000)\n"
              << "  --verify PATTERN         Verify data pattern: none|sequential|random|zero|ones (default: none)\n"
              << "  --checksum               Calculate checksums for each frame\n"
              << "  --batch-size NUM         Read frames in batches (default: 1)\n"
              << "  --json-output            Output results in JSON format\n"
              << "  -v, --verbose            Verbose output\n"
              << "  -h, --help               Show this help message\n";
}

int main(int argc, char* argv[]) {
    // Initialize logging with debug level for testing
    zerobuffer::init_logging(zerobuffer::debug);
    
    // Default values
    int frames = 1000;
    int frame_size = 1024;
    int timeout_ms = 5000;
    std::string verify_pattern = "none";
    bool calculate_checksums = false;
    int batch_size = 1;
    bool json_output = false;
    bool verbose = false;
    
    // Long options
    static struct option long_options[] = {
        {"frames", required_argument, 0, 'n'},
        {"size", required_argument, 0, 's'},
        {"timeout-ms", required_argument, 0, 0},
        {"verify", required_argument, 0, 0},
        {"checksum", no_argument, 0, 0},
        {"batch-size", required_argument, 0, 0},
        {"json-output", no_argument, 0, 0},
        {"verbose", no_argument, 0, 'v'},
        {"help", no_argument, 0, 'h'},
        {0, 0, 0, 0}
    };
    
    int option_index = 0;
    int c;
    
    while ((c = getopt_long(argc, argv, "n:s:vh", long_options, &option_index)) != -1) {
        switch (c) {
            case 0:
                // Long option
                if (strcmp(long_options[option_index].name, "timeout-ms") == 0) {
                    timeout_ms = std::stoi(optarg);
                } else if (strcmp(long_options[option_index].name, "verify") == 0) {
                    verify_pattern = optarg;
                } else if (strcmp(long_options[option_index].name, "checksum") == 0) {
                    calculate_checksums = true;
                } else if (strcmp(long_options[option_index].name, "batch-size") == 0) {
                    batch_size = std::stoi(optarg);
                } else if (strcmp(long_options[option_index].name, "json-output") == 0) {
                    json_output = true;
                }
                break;
            case 'n':
                frames = std::stoi(optarg);
                break;
            case 's':
                frame_size = std::stoi(optarg);
                break;
            case 'v':
                verbose = true;
                break;
            case 'h':
                print_usage(argv[0]);
                return 0;
            default:
                print_usage(argv[0]);
                return 1;
        }
    }
    
    if (optind >= argc) {
        std::cerr << "Error: buffer name required\n";
        print_usage(argv[0]);
        return 1;
    }
    
    std::string buffer_name = argv[optind];
    
    // Result object
    json result = {
        {"operation", "read"},
        {"buffer_name", buffer_name},
        {"frames_read", 0},
        {"frame_size", frame_size},
        {"metadata_size", 0},
        {"duration_seconds", 0.0},
        {"throughput_mbps", 0.0},
        {"verification_errors", 0},
        {"checksums", json::array()},
        {"errors", json::array()}
    };
    
    try {
        if (verbose && !json_output) {
            std::cout << "[READER] Starting reader process" << std::endl;
            std::cout << "[READER] Buffer name: " << buffer_name << std::endl;
            std::cout << "[READER] Expected frame size: " << frame_size << " bytes" << std::endl;
            std::cout << "[READER] Verification pattern: " << verify_pattern << std::endl;
            std::cout << "[READER] Frames to read: " << (frames >= 0 ? std::to_string(frames) : "unlimited") << std::endl;
            std::cout << "[READER] Timeout: " << timeout_ms << "ms" << std::endl;
        }
        
        // Create buffer with config matching benchmarks (256MB)
        zerobuffer::BufferConfig config(4096, 256 * 1024 * 1024);
        zerobuffer::Reader reader(buffer_name, config);
        
        if (verbose && !json_output) {
            std::cout << "[READER] Successfully created buffer" << std::endl;
        }
        
        // Read metadata if available
        auto metadata = reader.get_metadata();
        if (!metadata.empty()) {
            result["metadata_size"] = metadata.size();
            if (verbose && !json_output) {
                std::cout << "[READER] Read metadata: " << metadata.size() << " bytes" << std::endl;
            }
        }
        
        // Read frames
        auto start_time = std::chrono::high_resolution_clock::now();
        int frames_to_read = (frames >= 0) ? frames : INT_MAX;
        int frame_index = 0;
        
        if (verbose && !json_output) {
            std::cout << "[READER] Waiting for writer to connect..." << std::endl;
        }
        
        // Wait for writer to connect (with 30 second timeout)
        if (!reader.is_writer_connected(30000)) {
            if (verbose && !json_output) {
                std::cout << "[READER] No writer connected after 30 seconds timeout" << std::endl;
            }
            result.errors.push_back("Timeout waiting for writer connection");
            print_results(result, json_output);
            return 1;
        }
        
        if (verbose && !json_output) {
            std::cout << "[READER] Writer connected, starting to read frames..." << std::endl;
        }
        
        while (frame_index < frames_to_read) {
            try {
                // Check if writer is still connected
                if (!reader.is_writer_connected()) {
                    if (verbose && !json_output) {
                        std::cout << "[READER] Writer disconnected after " << frame_index << " frames" << std::endl;
                    }
                    break;
                }
                
                // Read frame with timeout
                // Note: C++ API doesn't have timeout parameter, uses blocking read
                auto frame = reader.read_frame();
                
                if (!frame.valid()) {
                    // Timeout or no more frames
                    if (verbose && !json_output) {
                        std::cout << "[READER] No more frames after " << frame_index << " frames" << std::endl;
                    }
                    break;
                }
                
                // Verify frame size
                if (frame.size() != static_cast<size_t>(frame_size)) {
                    result["errors"].push_back(
                        "Frame " + std::to_string(frame_index) + 
                        ": Expected size " + std::to_string(frame_size) + 
                        ", got " + std::to_string(frame.size())
                    );
                }
                
                // Create vector from frame data for verification
                const uint8_t* data_ptr = static_cast<const uint8_t*>(frame.data());
                std::vector<uint8_t> frame_data(data_ptr, data_ptr + frame.size());
                
                // Verify data pattern if requested
                if (verify_pattern != "none") {
                    if (!verify_frame_data(frame_data, frame_index, verify_pattern)) {
                        result["verification_errors"] = result["verification_errors"].get<int>() + 1;
                        if (verbose && !json_output) {
                            std::cout << "[READER] Frame " << frame_index << ": Verification failed" << std::endl;
                        }
                    }
                }
                
                // Calculate checksum if requested
                if (calculate_checksums) {
                    std::string checksum = calculate_checksum(frame_data);
                    if (result["checksums"].size() < 100) { // Limit stored checksums
                        result["checksums"].push_back({
                            {"frame", frame_index},
                            {"checksum", checksum}
                        });
                    }
                }
                
                frame_index++;
                result["frames_read"] = frame_index;
                
                if (verbose && !json_output) {
                    if (frame_index % 10 == 0 || frame_index == 1 || frame_index == frames_to_read) {
                        std::cout << "[READER] Read frame " << frame_index << std::endl;
                    }
                }
                
                // Release the frame
                reader.release_frame(frame);
                
            } catch (const zerobuffer::WriterDeadException& e) {
                if (verbose && !json_output) {
                    std::cout << "[READER] Writer died after " << frame_index << " frames" << std::endl;
                }
                break;
            } catch (const std::exception& e) {
                result["errors"].push_back(
                    "Frame " + std::to_string(frame_index) + ": " + e.what()
                );
                break;
            }
        }
        
        auto end_time = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration<double>(end_time - start_time).count();
        result["duration_seconds"] = duration;
        
        // Calculate throughput
        double total_mb = (result["frames_read"].get<int>() * frame_size) / (1024.0 * 1024.0);
        double throughput = (duration > 0) ? total_mb / duration : 0;
        result["throughput_mbps"] = throughput;
        
        if (!json_output) {
            std::cout << "[READER] Completed: read " << result["frames_read"] << " frames in " 
                      << std::fixed << std::setprecision(2) << duration << " seconds" << std::endl;
            std::cout << "[READER] Throughput: " << std::fixed << std::setprecision(2) 
                      << throughput << " MB/s" << std::endl;
            if (verify_pattern != "none") {
                std::cout << "[READER] Verification errors: " << result["verification_errors"] << std::endl;
            }
        }
        
    } catch (const std::exception& e) {
        result["errors"].push_back(e.what());
        
        if (json_output) {
            std::cout << result.dump(2) << std::endl;
        } else {
            std::cerr << "[READER] Error: " << e.what() << std::endl;
        }
        
        return 2;
    }
    
    if (json_output) {
        std::cout << result.dump(2) << std::endl;
    }
    
    return (result["verification_errors"].get<int>() == 0 && result["errors"].empty()) ? 0 : 1;
}