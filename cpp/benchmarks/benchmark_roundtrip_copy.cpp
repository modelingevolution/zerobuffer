#include <zerobuffer/zerobuffer.h>
#include <zerobuffer/platform.h>
#include <iostream>
#include <iomanip>
#include <chrono>
#include <vector>
#include <algorithm>
#include <numeric>
#include <thread>
#include <atomic>
#include <csignal>
#include <unistd.h>
#include <sys/wait.h>
#include <cstring>

using namespace zerobuffer;
using namespace std::chrono;

// Frame structure matching C# implementation
#pragma pack(push, 1)
struct TimestampedFrame {
    int64_t timestamp;    // 8 bytes - high resolution timestamp
    int32_t frame_id;     // 4 bytes
    int32_t padding;      // 4 bytes - align to 16 bytes
    // Followed by YUV420 data
};
#pragma pack(pop)

// Constants
constexpr size_t YUV420_FRAME_SIZE = 1920 * 1080 * 3 / 2;  // 3,110,400 bytes
constexpr size_t HEADER_SIZE = sizeof(TimestampedFrame);
constexpr size_t TOTAL_FRAME_SIZE = HEADER_SIZE + YUV420_FRAME_SIZE;
constexpr size_t BUFFER_SIZE = 256 * 1024 * 1024;  // 256 MB
constexpr int WARMUP_FRAMES = 10;  // Reduced for faster testing

// Test configurations
struct TestConfig {
    int target_fps;
    int frame_count;
};

const std::vector<TestConfig> TEST_CONFIGS = {
    {30, 100},   // Further reduced for testing
    {60, 100},   // Further reduced for testing
    //{120, 1000},
    //{240, 1000},
    //{500, 1000},
    //{1000, 1000}
};

// Forward declarations
double ticks_to_microseconds(int64_t ticks);

// Get high-resolution timestamp in ticks (platform specific)
int64_t get_timestamp_ticks() {
    auto now = high_resolution_clock::now();
    return now.time_since_epoch().count();
}

// Convert ticks to microseconds
double ticks_to_microseconds(int64_t ticks) {
    // Get the tick frequency
    using period = high_resolution_clock::period;
    double ticks_per_second = static_cast<double>(period::den) / period::num;
    return (ticks * 1000000.0) / ticks_per_second;
}

// High-precision periodic timer
class PeriodicTimer {
private:
    high_resolution_clock::time_point next_tick_;
    std::chrono::microseconds interval_;
    
public:
    explicit PeriodicTimer(std::chrono::microseconds interval) 
        : interval_(interval) {
        // Minimum 1ms interval like C#
        if (interval_ < microseconds(1000)) {
            interval_ = microseconds(1000);
        }
        next_tick_ = high_resolution_clock::now() + interval_;
    }
    
    void wait_for_next_tick() {
        auto now = high_resolution_clock::now();
        
        // If we're running late, just update next tick
        if (now >= next_tick_) {
            // Calculate how many intervals we missed
            auto elapsed = duration_cast<microseconds>(now - next_tick_);
            auto missed_intervals = elapsed.count() / interval_.count() + 1;
            next_tick_ += interval_ * missed_intervals;
            return;
        }
        
        // Busy wait for the last microsecond for precision
        auto wait_until = next_tick_ - microseconds(1000);
        if (now < wait_until) {
            std::this_thread::sleep_until(wait_until);
        }
        
        // Busy wait for final precision
        while (high_resolution_clock::now() < next_tick_) {
            std::this_thread::yield();
        }
        
        next_tick_ += interval_;
    }
};

// Latency measurement class
class LatencyBenchmark {
private:
    std::vector<double> latencies_;
    high_resolution_clock::time_point start_time_;
    
public:
    void start() {
        start_time_ = high_resolution_clock::now();
        latencies_.clear();
        latencies_.reserve(2000);  // Pre-allocate
    }
    
    void record_latency(int64_t send_ticks, int64_t receive_ticks) {
        double latency_us = ticks_to_microseconds(receive_ticks - send_ticks);
        latencies_.push_back(latency_us);
    }
    
    void print_results(int target_fps, int frames_sent, int frames_received) {
        std::cout << "  Frames sent: " << frames_sent 
                  << ", received: " << frames_received << std::endl;
        
        if (latencies_.empty()) {
            std::cout << "  No data collected!" << std::endl;
            return;
        }
        
        // Sort for percentiles
        std::sort(latencies_.begin(), latencies_.end());
        
        // Calculate statistics
        double min_latency = latencies_.front();
        double max_latency = latencies_.back();
        double avg_latency = std::accumulate(latencies_.begin(), latencies_.end(), 0.0) / latencies_.size();
        
        size_t p50_idx = latencies_.size() * 50 / 100;
        size_t p90_idx = latencies_.size() * 90 / 100;
        size_t p99_idx = latencies_.size() * 99 / 100;
        
        double p50_latency = latencies_[p50_idx];
        double p90_latency = latencies_[p90_idx];
        double p99_latency = latencies_[p99_idx];
        
        std::cout << "  Round-trip latency (microseconds):" << std::endl;
        std::cout << "    Min:    " << std::fixed << std::setprecision(0) << min_latency << " μs" << std::endl;
        std::cout << "    Avg:    " << std::fixed << std::setprecision(0) << avg_latency << " μs" << std::endl;
        std::cout << "    P50:    " << std::fixed << std::setprecision(0) << p50_latency << " μs" << std::endl;
        std::cout << "    P90:    " << std::fixed << std::setprecision(0) << p90_latency << " μs" << std::endl;
        std::cout << "    P99:    " << std::fixed << std::setprecision(0) << p99_latency << " μs" << std::endl;
        std::cout << "    Max:    " << std::fixed << std::setprecision(0) << max_latency << " μs" << std::endl;
    }
};

void run_benchmark_at_fps(int target_fps, int frame_count) {
    std::cout << "\n--- Testing at " << target_fps << " FPS ---" << std::endl;
    
    // Create buffers outside try block so they stay alive
    std::unique_ptr<Reader> reader;
    std::unique_ptr<Writer> writer;
    
    try {
        // Create Buffer B as Reader (output buffer)
        BufferConfig config(0, BUFFER_SIZE);
        reader = std::make_unique<Reader>("buffer-b", config);
        std::cout << "  Created buffer-b as Reader" << std::endl;
        
        // Give relay more time to initialize and connect
        std::this_thread::sleep_for(milliseconds(1000));
        
        // Connect to Buffer A as Writer (input buffer)
        writer = std::make_unique<Writer>("buffer-a");
        std::cout << "  Connected to buffer-a as Writer" << std::endl;
        
        // Prepare frame data
        std::vector<uint8_t> frame_data(TOTAL_FRAME_SIZE);
        // Fill with some pattern (optional)
        for (size_t i = 0; i < frame_data.size(); ++i) {
            frame_data[i] = static_cast<uint8_t>(i & 0xFF);
        }
        
        // Calculate frame interval
        auto frame_interval = microseconds(1000000 / target_fps);
        PeriodicTimer timer(frame_interval);
        
        // Start a receiver thread for warmup too
        std::atomic<int> warmup_received(0);
        std::atomic<bool> warmup_done(false);
        std::thread warmup_receiver([&]() {
            while (!warmup_done && warmup_received < WARMUP_FRAMES) {
                try {
                    Frame frame = reader->read_frame(std::chrono::seconds(5));
                    if (frame.valid()) {
                        if (frame.size() >= HEADER_SIZE) {
                            const TimestampedFrame* header = 
                                reinterpret_cast<const TimestampedFrame*>(frame.data());
                            if (header->frame_id < 0) {  // Warmup frame
                                warmup_received++;
                            }
                        }
                        reader->release_frame(frame);
                    }
                } catch (const std::exception& e) {
                    break;
                }
            }
        });
        
        // Warmup - using copy-based method like C#
        std::cout << "  Warming up... " << std::flush;
        for (int i = 0; i < WARMUP_FRAMES; ++i) {
            // Get timestamp right before sending
            int64_t send_ticks = get_timestamp_ticks();
            
            // Prepare frame like C# does
            TimestampedFrame* header = reinterpret_cast<TimestampedFrame*>(frame_data.data());
            header->timestamp = send_ticks;
            header->frame_id = -(i+1);  // Negative for warmup frames
            header->padding = 0;
            
            // Send using copy-based method like C#
            writer->write_frame(frame_data);
            
            timer.wait_for_next_tick();
        }
        
        // Wait for warmup frames to be received
        auto warmup_timeout = high_resolution_clock::now() + seconds(5);
        while (warmup_received < WARMUP_FRAMES && high_resolution_clock::now() < warmup_timeout) {
            std::this_thread::sleep_for(milliseconds(10));
        }
        
        warmup_done = true;
        warmup_receiver.join();
        std::cout << "done" << std::endl;
        
        // Measurement
        LatencyBenchmark benchmark;
        benchmark.start();
        
        int frames_to_send = frame_count;
        int frames_sent = 0;
        int frames_received = 0;
        
        std::cout << "  Measuring " << frames_to_send << " frames... " << std::flush;
        
        // Start receiver thread
        std::atomic<bool> receiver_done(false);
        std::thread receiver([&]() {
            while (!receiver_done) {
                try {
                    Frame frame = reader->read_frame(std::chrono::seconds(5));
                    if (!frame.valid()) {
                        continue;
                    }
                    
                    // Get receive timestamp immediately
                    int64_t receive_ticks = get_timestamp_ticks();
                    
                    // Extract the timestamp from frame data
                    if (frame.size() >= HEADER_SIZE) {
                        const TimestampedFrame* header = 
                            reinterpret_cast<const TimestampedFrame*>(frame.data());
                        
                        // Process all frames (including warmup)
                        if (header->frame_id >= 0) {
                            benchmark.record_latency(header->timestamp, receive_ticks);
                            frames_received++;
                            
                            if (frames_received >= frames_to_send) {
                                receiver_done = true;
                            }
                        }
                    }
                    
                    reader->release_frame(frame);
                    
                } catch (const WriterDeadException&) {
                    break;
                } catch (const std::exception& e) {
                    std::cerr << "Receiver error: " << e.what() << std::endl;
                    break;
                }
            }
        });
        
        // Send frames using copy-based method like C#
        for (int i = 0; i < frames_to_send; ++i) {
            // Get timestamp right before sending
            int64_t send_ticks = get_timestamp_ticks();
            
            // Prepare frame like C# does
            TimestampedFrame* header = reinterpret_cast<TimestampedFrame*>(frame_data.data());
            header->timestamp = send_ticks;
            header->frame_id = i;
            header->padding = 0;
            
            // Send using copy-based method like C#
            writer->write_frame(frame_data);
            frames_sent++;
            
            timer.wait_for_next_tick();
        }
        
        // Wait for receiver to finish (with timeout)
        auto timeout = high_resolution_clock::now() + seconds(5);
        while (!receiver_done && high_resolution_clock::now() < timeout) {
            std::this_thread::sleep_for(milliseconds(10));
        }
        
        receiver_done = true;
        receiver.join();
        
        std::cout << "done" << std::endl;
        
        // Print results
        benchmark.print_results(target_fps, frames_sent, frames_received);
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
    }
}

pid_t start_relay_process() {
    pid_t relay_pid = fork();
    if (relay_pid == 0) {
        // Child process - execute relay
        execl("./relay_process", "relay_process", nullptr);
        std::cerr << "Failed to execute relay process" << std::endl;
        exit(1);
    }
    return relay_pid;
}

void stop_relay_process(pid_t relay_pid) {
    kill(relay_pid, SIGTERM);
    int status;
    waitpid(relay_pid, &status, 0);
    
    // Clean up shared memory resources
    // Note: On Linux, semaphore names don't have a leading slash in the filesystem
    try {
        SharedMemory::remove("buffer-a");
        SharedMemory::remove("buffer-b");
        Semaphore::remove("sem-w-buffer-a");
        Semaphore::remove("sem-r-buffer-a");
        Semaphore::remove("sem-w-buffer-b");
        Semaphore::remove("sem-r-buffer-b");
    } catch (...) {
        // Ignore cleanup errors
    }
}

int main() {
    std::cout << "ZeroBuffer Cross-Process Round-Trip Latency Benchmark (Copy-based)" << std::endl;
    std::cout << "==================================================================" << std::endl;
    std::cout << "Frame size: " << TOTAL_FRAME_SIZE << " bytes "
              << "(YUV420 1920x1080 + 16-byte header)" << std::endl;
    std::cout << "Buffer size: " << (BUFFER_SIZE / 1024 / 1024) << " MB" << std::endl;
    
    // Initial cleanup to ensure we start fresh
    try {
        SharedMemory::remove("buffer-a");
        SharedMemory::remove("buffer-b");
        Semaphore::remove("sem-w-buffer-a");
        Semaphore::remove("sem-r-buffer-a");
        Semaphore::remove("sem-w-buffer-b");
        Semaphore::remove("sem-r-buffer-b");
    } catch (...) {
        // Ignore cleanup errors
    }
    
    // Run benchmarks at different FPS levels
    for (const auto& config : TEST_CONFIGS) {
        // Start fresh relay process for each test
        pid_t relay_pid = start_relay_process();
        
        // Give relay time to initialize
        std::this_thread::sleep_for(seconds(2));
        
        // Run benchmark
        run_benchmark_at_fps(config.target_fps, config.frame_count);
        
        // Stop relay
        stop_relay_process(relay_pid);
        
        // Brief pause between tests
        std::this_thread::sleep_for(seconds(1));
    }
    
    std::cout << "\nBenchmark complete!" << std::endl;
    return 0;
}