# ZeroBuffer Improvements TODO

This file tracks suggested improvements from code reviews that need to be implemented.

## Documentation Improvements

### 1. Add Performance Characteristics to README.md
- [ ] Add latency benchmarks and guarantees
- [ ] Add throughput specifications
- [ ] Add guidance on optimal frame sizes
- [ ] Add CPU/memory overhead information
- [ ] Add memory usage formula: `total_memory = sizeof(OIEB) + metadata_size + payload_size + OS_overhead`

### 2. Add Error Recovery Patterns to README.md
- [ ] Document Writer Detected Reader Death pattern
- [ ] Document Reader Detected Writer Death pattern
- [ ] Add orchestration guidance

### 3. Clarify Protocol Specifications
- [ ] Better document OIEB write/read position semantics
- [ ] Add dedicated wrap-around behavior section
- [ ] Clarify frame format with size constraints

## Code Improvements

### 1. Enhance Existing Benchmarks
- [x] C++ has comprehensive benchmarks (latency, throughput, scenarios)
- [x] C# has round-trip benchmark with relay
- [x] Python has round-trip benchmark
- [ ] Add memory barrier cost micro-benchmark
- [ ] Add semaphore signaling overhead micro-benchmark
- [ ] Create unified benchmark result comparison tool

### 2. Implement C++ General Purpose Relay
- [ ] Create zerobuffer-test-relay matching C#/Python interface
- [ ] Support all relay options (transform, create-output, etc.)
- [ ] Enable complete cross-platform relay testing

### 3. Implement Health Monitoring
- [ ] Add HealthMonitor class
- [ ] Track buffer utilization
- [ ] Track wrap counts
- [ ] Add degradation detection

### 4. Add Diagnostic Dumps
- [ ] Implement dump_state() methods
- [ ] Add buffer state visualization
- [ ] Add performance metrics logging

### 5. Optimize Hot Paths
- [ ] Add likely/unlikely branch hints
- [ ] Consider cache line padding for OIEB
- [ ] Profile and optimize semaphore operations

## Testing Improvements

### 1. Add Missing Valid Tests
- [ ] Burst write scenario test (Test 6.2)
- [ ] Slow reader/fast writer test (Test 6.3)
- [ ] Resource exhaustion test

### 2. Add Edge Case Tests
- [ ] Power-of-2 frame sizes at boundaries
- [ ] Metadata-only operations
- [ ] Rapid connect/disconnect cycles
- [ ] Memory pressure scenarios
- [ ] Zero-sized metadata block
- [ ] Single-byte buffer sizes
- [ ] Frames exactly matching buffer size

### 3. Add Stress Tests
- [ ] Long-running stability tests (24+ hours)
- [ ] High-frequency operations (>10k ops/sec)
- [ ] Large frame sizes (>100MB)
- [ ] Multi-process scenarios (>10 processes)

### 4. Improve Test Infrastructure
- [ ] Create unified test coverage reporting
- [ ] Add cross-platform CI pipeline
- [ ] Standardize test naming across languages
- [ ] Add performance regression tests

## Future Enhancements

### 1. Implement Duplex Channel Design
- [x] Implement IDuplexClient interface in C#
- [x] Implement IImmutableDuplexServer and IMutableDuplexServer in C#
- [x] Create DuplexChannelFactory in C#
- [x] Add comprehensive duplex channel tests in C#
- [x] Support true zero-copy mutable processing with GetMutableSpan()
- [ ] Implement duplex channel in C++
- [ ] Implement duplex channel in Python
- [ ] Document usage patterns
- [ ] Add async support (requires async semaphores with custom awaiters)

### 2. Implement Payload Sharing (Zero-Copy Relay)
- [ ] Extend Reader constructor with allow_payload_sharing flag
- [ ] Extend Writer constructor with request_payload_sharing flag
- [ ] Add SharedWriter class
- [ ] Extend OIEB with payload sharing fields
- [ ] Implement reference counting for shared payloads
- [ ] Add safety mechanisms to prevent use-after-free

### 3. Multi-Reader Support
- [ ] Design shared reader semaphore scheme
- [ ] Add reader registration/tracking
- [ ] Implement fair reader scheduling

### 4. Network Transport
- [ ] Add TCP/UDP transport layer
- [ ] Implement compression options
- [ ] Add encryption support

### 5. Monitoring & Observability
- [ ] Add OpenTelemetry integration
- [ ] Add Prometheus metrics
- [ ] Add structured logging

## Priority Order

1. **High Priority**: Documentation improvements (clarify existing behavior)
2. **Medium Priority**: Benchmark tests (measure current performance)
3. **Low Priority**: Code optimizations (after benchmarks establish baselines)
4. **Future**: Multi-reader and network transport