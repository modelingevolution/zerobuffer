#include <zerobuffer/zerobuffer.h>
#include <iostream>
#include <chrono>
#include <vector>
#include <numeric>
#include <algorithm>
#include <cstring>
#include <thread>

using namespace zerobuffer;
using namespace std::chrono;

// Frame structure
#pragma pack(push, 1)
struct TimestampedFrame {
    int64_t timestamp;
    int32_t frame_id;
    int32_t padding;
};
#pragma pack(pop)

constexpr size_t YUV420_FRAME_SIZE = 1920 * 1080 * 3 / 2;
constexpr size_t HEADER_SIZE = sizeof(TimestampedFrame);
constexpr size_t TOTAL_FRAME_SIZE = HEADER_SIZE + YUV420_FRAME_SIZE;
constexpr size_t BUFFER_SIZE = 256 * 1024 * 1024;

int64_t get_timestamp_ticks() {
    return high_resolution_clock::now().time_since_epoch().count();
}

double ticks_to_microseconds(int64_t ticks) {
    using period = high_resolution_clock::period;
    double ticks_per_second = static_cast<double>(period::den) / period::num;
    return (ticks * 1000000.0) / ticks_per_second;
}

void benchmark_method(const std::string& name, bool use_zerocopy) {
    std::cout << "\n=== " << name << " ===" << std::endl;
    
    try {
        // Clean up
        try {
            SharedMemory::remove("test-buffer");
            Semaphore::remove("sem-w-test-buffer");
            Semaphore::remove("sem-r-test-buffer");
        } catch(...) {}
        
        // Create buffer
        BufferConfig config(0, BUFFER_SIZE);
        Reader reader("test-buffer", config);
        
        // Start writer thread
        std::thread writer_thread([use_zerocopy]() {
            std::this_thread::sleep_for(milliseconds(100));
            Writer writer("test-buffer");
            
            std::vector<uint8_t> frame_data(TOTAL_FRAME_SIZE);
            
            // Measure write time for 100 frames
            auto start = high_resolution_clock::now();
            
            for (int i = 0; i < 100; i++) {
                if (use_zerocopy) {
                    // Zero-copy method
                    uint64_t sequence;
                    void* buffer = writer.get_frame_buffer(TOTAL_FRAME_SIZE, sequence);
                    
                    // Write header directly
                    TimestampedFrame* header = reinterpret_cast<TimestampedFrame*>(buffer);
                    header->timestamp = get_timestamp_ticks();
                    header->frame_id = i;
                    header->padding = 0;
                    // YUV data area is left uninitialized for benchmark
                    
                    writer.commit_frame();
                } else {
                    // Copy-based method (like C#)
                    TimestampedFrame* header = reinterpret_cast<TimestampedFrame*>(frame_data.data());
                    header->timestamp = get_timestamp_ticks();
                    header->frame_id = i;
                    header->padding = 0;
                    
                    writer.write_frame(frame_data);
                }
            }
            
            auto end = high_resolution_clock::now();
            auto duration = duration_cast<microseconds>(end - start);
            std::cout << "  Write time for 100 frames: " << duration.count() << " μs" << std::endl;
            std::cout << "  Average per frame: " << duration.count() / 100.0 << " μs" << std::endl;
        });
        
        // Measure read time
        std::vector<double> latencies;
        auto start = high_resolution_clock::now();
        
        for (int i = 0; i < 100; i++) {
            Frame frame = reader.read_frame();
            int64_t receive_ticks = get_timestamp_ticks();
            
            if (frame.size() >= HEADER_SIZE) {
                const TimestampedFrame* header = 
                    reinterpret_cast<const TimestampedFrame*>(frame.data());
                double latency = ticks_to_microseconds(receive_ticks - header->timestamp);
                latencies.push_back(latency);
            }
            
            reader.release_frame(frame);
        }
        
        auto end = high_resolution_clock::now();
        auto duration = duration_cast<microseconds>(end - start);
        
        writer_thread.join();
        
        // Calculate stats
        std::sort(latencies.begin(), latencies.end());
        double avg_latency = std::accumulate(latencies.begin(), latencies.end(), 0.0) / latencies.size();
        double p50 = latencies[latencies.size() * 50 / 100];
        double p90 = latencies[latencies.size() * 90 / 100];
        
        std::cout << "  Read time for 100 frames: " << duration.count() << " μs" << std::endl;
        std::cout << "  Average per frame: " << duration.count() / 100.0 << " μs" << std::endl;
        std::cout << "  In-process latency - Avg: " << avg_latency 
                  << " μs, P50: " << p50 
                  << " μs, P90: " << p90 << " μs" << std::endl;
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
    }
}

int main() {
    std::cout << "ZeroBuffer Method Comparison" << std::endl;
    std::cout << "Frame size: " << TOTAL_FRAME_SIZE << " bytes" << std::endl;
    
    benchmark_method("Copy-based (like C#)", false);
    benchmark_method("Zero-copy (direct write)", true);
    
    // Clean up
    try {
        SharedMemory::remove("test-buffer");
        Semaphore::remove("sem-w-test-buffer");
        Semaphore::remove("sem-r-test-buffer");
    } catch(...) {}
    
    return 0;
}