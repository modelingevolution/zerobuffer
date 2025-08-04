#include <zerobuffer/zerobuffer.h>
#include <zerobuffer/logger.h>
#include <iostream>
#include <vector>
#include <chrono>
#include <cstring>
#include <getopt.h>
#include <thread>
#include <nlohmann/json.hpp>

using json = nlohmann::json;
using namespace zerobuffer;

struct WriterConfig {
    std::string buffer_name;
    int frames = 1000;
    size_t frame_size = 1024;
    std::string metadata;
    std::string metadata_file;
    std::string pattern = "sequential";
    int delay_ms = 0;
    int batch_size = 1;
    bool json_output = false;
    bool verbose = false;
};

void print_usage(const char* program) {
    std::cout << "Usage: " << program << " <buffer-name> [options]\n"
              << "\nRequired:\n"
              << "  buffer-name              Name of the buffer to write to\n"
              << "\nOptions:\n"
              << "  --frames, -n COUNT      Number of frames to write (default: 1000)\n"
              << "  --size, -s BYTES        Size of each frame in bytes (default: 1024)\n"
              << "  --metadata, -m TEXT     Metadata to write (optional)\n"
              << "  --metadata-file FILE    Read metadata from file (optional)\n"
              << "  --pattern PATTERN       Data pattern: sequential|random|zero|ones (default: sequential)\n"
              << "  --delay-ms MILLIS       Delay between frames in milliseconds (default: 0)\n"
              << "  --batch-size COUNT      Write frames in batches (default: 1)\n"
              << "  --json-output           Output results in JSON format\n"
              << "  --verbose, -v           Verbose output\n"
              << "  --help, -h              Show help message\n";
}

void fill_frame_data(std::vector<uint8_t>& data, int frame_index, const std::string& pattern) {
    if (pattern == "sequential") {
        for (size_t i = 0; i < data.size(); ++i) {
            data[i] = (frame_index + i) % 256;
        }
    } else if (pattern == "random") {
        std::srand(frame_index);
        for (size_t i = 0; i < data.size(); ++i) {
            data[i] = std::rand() % 256;
        }
    } else if (pattern == "zero") {
        std::fill(data.begin(), data.end(), 0);
    } else if (pattern == "ones") {
        std::fill(data.begin(), data.end(), 0xFF);
    }
}

int main(int argc, char* argv[]) {
    WriterConfig config;
    
    // Initialize logging with debug level for testing
    zerobuffer::init_logging(zerobuffer::debug);
    
    // Parse command line
    static struct option long_options[] = {
        {"frames", required_argument, 0, 'n'},
        {"size", required_argument, 0, 's'},
        {"metadata", required_argument, 0, 'm'},
        {"metadata-file", required_argument, 0, 0},
        {"pattern", required_argument, 0, 0},
        {"delay-ms", required_argument, 0, 0},
        {"batch-size", required_argument, 0, 0},
        {"json-output", no_argument, 0, 0},
        {"verbose", no_argument, 0, 'v'},
        {"help", no_argument, 0, 'h'},
        {0, 0, 0, 0}
    };
    
    int opt;
    int option_index = 0;
    
    while ((opt = getopt_long(argc, argv, "n:s:m:vh", long_options, &option_index)) != -1) {
        switch (opt) {
            case 0:
                if (std::string(long_options[option_index].name) == "metadata-file") {
                    config.metadata_file = optarg;
                } else if (std::string(long_options[option_index].name) == "pattern") {
                    config.pattern = optarg;
                } else if (std::string(long_options[option_index].name) == "delay-ms") {
                    config.delay_ms = std::atoi(optarg);
                } else if (std::string(long_options[option_index].name) == "batch-size") {
                    config.batch_size = std::atoi(optarg);
                } else if (std::string(long_options[option_index].name) == "json-output") {
                    config.json_output = true;
                }
                break;
            case 'n':
                config.frames = std::atoi(optarg);
                break;
            case 's':
                config.frame_size = std::atoi(optarg);
                break;
            case 'm':
                config.metadata = optarg;
                break;
            case 'v':
                config.verbose = true;
                break;
            case 'h':
                print_usage(argv[0]);
                return 0;
            default:
                print_usage(argv[0]);
                return 3;
        }
    }
    
    if (optind >= argc) {
        std::cerr << "Error: Buffer name required\n";
        print_usage(argv[0]);
        return 3;
    }
    
    config.buffer_name = argv[optind];
    
    // Initialize result
    json result;
    result["operation"] = "write";
    result["buffer_name"] = config.buffer_name;
    result["frames_written"] = 0;
    result["frame_size"] = config.frame_size;
    result["metadata_size"] = 0;
    result["errors"] = json::array();
    
    try {
        // Connect to buffer
        if (config.verbose && !config.json_output) {
            std::cout << "[WRITER] Starting writer process" << std::endl;
            std::cout << "[WRITER] Buffer name: " << config.buffer_name << std::endl;
            std::cout << "[WRITER] Frame size: " << config.frame_size << " bytes" << std::endl;
            std::cout << "[WRITER] Pattern: " << config.pattern << std::endl;
            std::cout << "[WRITER] Frames to write: " << config.frames << std::endl;
            std::cout << "[WRITER] Delay between frames: " << config.delay_ms << "ms" << std::endl;
        }
        
        Writer writer(config.buffer_name);
        
        if (config.verbose && !config.json_output) {
            std::cout << "[WRITER] Successfully connected to buffer" << std::endl;
        }
        
        // Write metadata if provided
        if (!config.metadata.empty()) {
            writer.set_metadata(config.metadata.data(), config.metadata.size());
            result["metadata_size"] = config.metadata.size();
            if (config.verbose && !config.json_output) {
                std::cout << "[WRITER] Wrote metadata: " << config.metadata.size() << " bytes" << std::endl;
            }
        }
        
        // Prepare frame data
        std::vector<uint8_t> frame_data(config.frame_size);
        
        // Write frames
        auto start_time = std::chrono::high_resolution_clock::now();
        
        if (config.verbose && !config.json_output) {
            std::cout << "[WRITER] Starting to write frames..." << std::endl;
        }
        
        for (int i = 0; i < config.frames; ++i) {
            fill_frame_data(frame_data, i, config.pattern);
            writer.write_frame(frame_data.data(), frame_data.size());
            result["frames_written"] = i + 1;
            
            if (config.verbose && !config.json_output) {
                if ((i + 1) % 10 == 0 || i == 0 || i == config.frames - 1) {
                    std::cout << "[WRITER] Wrote frame " << (i + 1) << "/" << config.frames << std::endl;
                }
            }
            
            if (config.delay_ms > 0) {
                std::this_thread::sleep_for(std::chrono::milliseconds(config.delay_ms));
            }
        }
        
        auto end_time = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration<double>(end_time - start_time);
        
        result["duration_seconds"] = duration.count();
        
        // Calculate throughput
        double total_mb = (config.frames * config.frame_size) / (1024.0 * 1024.0);
        double throughput = total_mb / duration.count();
        result["throughput_mbps"] = throughput;
        
        if (!config.json_output) {
            std::cout << "[WRITER] Completed: wrote " << config.frames << " frames in " 
                      << duration.count() << " seconds" << std::endl;
            std::cout << "[WRITER] Throughput: " << throughput << " MB/s" << std::endl;
        }
        
    } catch (const std::exception& e) {
        result["errors"].push_back(e.what());
        
        if (config.json_output) {
            std::cout << result.dump(2) << std::endl;
        } else {
            std::cerr << "[WRITER] Error: " << e.what() << std::endl;
        }
        
        return 2;
    }
    
    if (config.json_output) {
        std::cout << result.dump(2) << std::endl;
    }
    
    return 0;
}