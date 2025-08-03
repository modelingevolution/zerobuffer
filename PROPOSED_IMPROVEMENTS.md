# Proposed Improvements for ZeroBuffer

## 1. Benchmark Tests to Add to TEST_SCENARIOS.md

```markdown
## 14. Benchmark Tests

### 14.1 Latency Benchmark
- Setup: Single reader/writer, various frame sizes (1KB, 64KB, 1MB, 10MB)
- Measure: Time from write_frame() start to read_frame() complete
- Operations: 10,000 iterations per frame size
- Report: Min, Max, Mean, P50, P90, P99, P99.9 latencies

### 14.2 Throughput Benchmark  
- Setup: Continuous write/read, no delays
- Frame sizes: 1KB, 64KB, 1MB, 10MB
- Duration: 60 seconds per size
- Report: Frames/sec, MB/sec, CPU usage %

### 14.3 Wrap-Around Overhead Benchmark
- Setup: Buffer size = 1.5x frame size (forces wrap-around)
- Measure: Performance degradation due to wrap-around
- Compare: With and without wrap-around

### 14.4 Memory Barrier Cost Benchmark
- Measure: Time for memory barrier operations
- Compare: With and without barriers
- Verify: Data integrity maintained

### 14.5 Semaphore Signaling Overhead
- Measure: Cost of sem_post/sem_wait operations
- Test: Various signal rates (1Hz to 10kHz)
- Report: Impact on latency and CPU usage
```

## 2. Health Monitoring Implementation

```cpp
// Add to reader.h and writer.h
class HealthMonitor {
public:
    struct Health {
        uint64_t buffer_capacity;
        uint64_t buffer_free_bytes;
        double utilization_percent;
        bool is_degraded;
        uint64_t frames_written;
        uint64_t frames_read;
        uint64_t wrap_count;
    };
    
    Health get_health() const {
        const OIEB* oieb = get_oieb();
        Health h;
        h.buffer_capacity = oieb->payload_size;
        h.buffer_free_bytes = oieb->payload_free_bytes;
        h.utilization_percent = 100.0 * (1.0 - (double)h.buffer_free_bytes / h.buffer_capacity);
        h.is_degraded = h.utilization_percent > 80.0;  // Degraded if >80% full
        h.frames_written = oieb->payload_written_count;
        h.frames_read = oieb->payload_read_count;
        // Track wraps via position resets
        return h;
    }
    
    void log_health_if_degraded() {
        auto h = get_health();
        if (h.is_degraded) {
            static auto last_log = std::chrono::steady_clock::now();
            auto now = std::chrono::steady_clock::now();
            if (now - last_log > std::chrono::seconds(10)) {  // Log every 10s max
                std::cerr << "[WARN] ZeroBuffer " << name_ 
                          << " degraded: " << h.utilization_percent 
                          << "% full (" << h.buffer_free_bytes 
                          << " bytes free)" << std::endl;
                last_log = now;
            }
        }
    }
};
```

## 3. Capacity Planning Section for README.md

```markdown
## Capacity Planning

### Buffer Size Calculation

For video streaming applications:

**Basic Formula:**
```
payload_size = max_frame_size * 3 + overhead
overhead = number_of_frames_in_flight * sizeof(FrameHeader)
```

**Examples:**

1. **1080p @ 30 FPS (6MB frames)**
   - max_frame_size = 6MB
   - frames_in_flight = 3 (100ms buffer)
   - payload_size = 6MB * 3 + 3 * 16 = 18MB + 48B ≈ 20MB

2. **4K @ 60 FPS (25MB frames)**
   - max_frame_size = 25MB  
   - frames_in_flight = 6 (100ms buffer)
   - payload_size = 25MB * 3 + 6 * 16 = 75MB + 96B ≈ 80MB

3. **Small telemetry data (1KB @ 1000Hz)**
   - max_frame_size = 1KB
   - frames_in_flight = 100 (100ms buffer)
   - payload_size = 1KB * 3 + 100 * 16 = 3KB + 1.6KB ≈ 5KB

**Rule of thumb:** 
- Buffer 3x your maximum frame size
- Add 16 bytes per expected concurrent frame
- Round up to next power of 2 for alignment
```

## 4. State Diagram Addition

```markdown
## State Diagrams

### Reader State Machine
```
┌─────────┐
│ Created │
└────┬────┘
     │ create_buffer()
┌────▼────┐
│  Idle   │◄─────────────┐
└────┬────┘              │
     │ wait_semaphore()  │
┌────▼────┐              │
│ Reading │              │
└────┬────┘              │
     │ read_frame()      │
┌────▼────────┐          │
│ Processing  │          │
└────┬────────┘          │
     │ release_frame()   │
     └───────────────────┘

Error transitions:
Any state → [Error] on:
- Writer dead (after 5s timeout)
- Memory corruption
- Invalid sequence
```

### Writer State Machine
```
┌────────────┐
│ Connecting │
└─────┬──────┘
      │ open_buffer()
      ▼
┌─────────────┐     ┌─────────┐
│  Connected  │────►│ Blocked │
└─────┬───────┘     └────┬────┘
      │                  │
      │ write_frame()    │ space_available
      ▼                  │
┌─────────────┐         │
│   Writing   │◄────────┘
└─────────────┘

Error transitions:
Connected/Blocked → [Error] on:
- Reader dead (after 5s timeout)
- No space after timeout
```
```

## 5. Simple Test for Health Monitoring

```cpp
TEST_F(ZeroBufferScenarioTest, HealthMonitoring) {
    const std::string buffer_name = GenerateTestName();
    BufferConfig config(0, 10 * 1024);  // 10KB buffer
    
    Reader reader(buffer_name, config);
    Writer writer(buffer_name);
    
    // Fill buffer to 85%
    std::vector<uint8_t> frame(8500, 0xAA);
    writer.write_frame(frame);
    
    // Check health
    auto health = writer.get_health();
    EXPECT_GT(health.utilization_percent, 80.0);
    EXPECT_TRUE(health.is_degraded);
    
    // Verify logging happens (would need to capture stderr)
    writer.log_health_if_degraded();
}
```

## Summary of Practical Improvements

1. **Add 5 benchmark tests** - Get real numbers before making performance claims
2. **Simple health monitoring** - Just expose buffer utilization % and log warnings
3. **Clear capacity formulas** - 3x max frame size + overhead
4. **State diagrams** - Visual representation of valid states
5. **Skip security** - Document it's for trusted internal use only

These changes are practical and directly address operational needs without over-engineering.