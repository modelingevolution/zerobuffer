# Duplex Protocol Proposition: In-Place Modification with Shared Payload

**Status: FUTURE PROPOSAL (v2.0.0) - NOT IMPLEMENTED**

**Current Implementation Status (v1.0.0):** The current duplex channel implementation uses separate buffers for request and response channels. See [API.md](API.md) for the current implementation details.

## Executive Summary

This document proposes a future extension (v2.0.0) to the ZeroBuffer protocol to support efficient request/response patterns using in-place modification with a shared payload buffer. This approach would reduce memory usage by 50% compared to current duplex channels while maintaining zero-copy performance.

## Motivation

Current duplex channel implementations use two separate buffers:
- Request buffer: Client → Server
- Response buffer: Server → Client

For scenarios where the server modifies request data to create responses (e.g., image processing, data transformation), this creates unnecessary memory duplication and copy operations.

## Proposed Architecture

### Core Concept

Use **two Operation Info Exchange Blocks (OIEBs)** with **one shared payload buffer**:

```
[OIEB_Request][OIEB_Response][Metadata_Req][Metadata_Resp][Shared Payload Buffer]
      ↑              ↑             ↑              ↑              ↑
   Request        Response     Request       Response      Shared by
   tracking       tracking     metadata      metadata        both
```

### Memory Layout

```
Total Shared Memory Segment:
+------------------+------------------+----------------+----------------+----------------------+
| OIEB_Request     | OIEB_Response    | Metadata_Req   | Metadata_Resp  | Payload Buffer      |
| (88 bytes)       | (88 bytes)       | (configurable) | (configurable) | (configurable)      |
+------------------+------------------+----------------+----------------+----------------------+
```

### Protocol Flow

1. **Client writes request**
   - Writes to shared payload at position X
   - Updates OIEB_Request: `written_bytes`, `written_count`
   - Signals `sem-request-ready`

2. **Server reads request**
   - Reads from position X using OIEB_Request
   - Updates OIEB_Request: `read_count`
   
3. **Server modifies in-place**
   - Modifies data at same position X
   - Updates OIEB_Response: `written_count` (marks as response ready)
   - Signals `sem-response-ready`

4. **Client reads response**
   - Reads from position X using OIEB_Response
   - Updates OIEB_Response: `read_count`
   - Updates both OIEBs: `free_bytes` (reclaim space)
   - Signals `sem-space-available`

### Semaphore Architecture

Four semaphores for complete synchronization:

1. **`sem-request-ready-{name}`** - Client → Server: New request available
2. **`sem-response-ready-{name}`** - Server → Client: Response ready at same location
3. **`sem-response-consumed-{name}`** - Client → Server: Response has been read
4. **`sem-space-available-{name}`** - Server → Client: Buffer space freed

## Implementation Design

### SharedMemoryView Abstraction

```cpp
// Base interface for memory providers
class IMemoryProvider {
public:
    virtual void* allocate(size_t size) = 0;
    virtual void* map(size_t offset, size_t size) = 0;
    virtual void unmap() = 0;
};

// Allocates new shared memory
class SharedMemory : public IMemoryProvider {
    void* allocate(size_t size) override;
    void* map(size_t offset, size_t size) override;
};

// Maps view of existing shared memory
class SharedMemoryView : public IMemoryProvider {
    void* base_address;
    
    SharedMemoryView(void* existing_memory) : base_address(existing_memory) {}
    void* allocate(size_t size) override { 
        throw std::runtime_error("Cannot allocate in view mode"); 
    }
    void* map(size_t offset, size_t size) override {
        return static_cast<char*>(base_address) + offset;
    }
};
```

### Modified BufferConfig

```cpp
struct BufferConfig {
    size_t metadata_size;
    size_t payload_size;
    std::unique_ptr<IMemoryProvider> memory_provider;
    
    // Standard constructor - allocates new memory
    BufferConfig(size_t meta, size_t payload) 
        : metadata_size(meta), payload_size(payload),
          memory_provider(std::make_unique<SharedMemory>()) {}
    
    // Reuse constructor - uses existing memory for payload
    BufferConfig(size_t meta, void* existing_payload)
        : metadata_size(meta), payload_size(0),
          memory_provider(std::make_unique<SharedMemoryView>(existing_payload)) {}
};
```

### InPlaceDuplexChannel Implementation

```cpp
class InPlaceDuplexChannel : public IDuplexChannel {
private:
    std::unique_ptr<Reader> request_reader;
    std::unique_ptr<Writer> request_writer;
    std::unique_ptr<Reader> response_reader;
    std::unique_ptr<Writer> response_writer;
    
public:
    InPlaceDuplexChannel(const std::string& name, const BufferConfig& config) {
        // Create request channel with full allocation
        request_reader = std::make_unique<Reader>(
            name + "_request", config
        );
        
        // Get pointer to request payload
        void* shared_payload = request_reader->get_payload_address();
        
        // Create response channel reusing request payload
        BufferConfig response_config(config.metadata_size, shared_payload);
        response_reader = std::make_unique<Reader>(
            name + "_response", response_config
        );
        
        // Create writers
        request_writer = std::make_unique<Writer>(name + "_request");
        response_writer = std::make_unique<Writer>(name + "_response");
    }
};
```

## API Extensions

### Factory Pattern Extension

```cpp
class IDuplexChannelFactory {
public:
    // Existing methods...
    
    // New method for in-place duplex channels
    virtual std::unique_ptr<IInPlaceDuplexServer> createInPlaceDuplexServer(
        const std::string& channel_name,
        const BufferConfig& config) = 0;
};

// Server that processes requests in-place
class IInPlaceDuplexServer : public IDuplexServer {
public:
    // Handler modifies frame in-place
    virtual void start(std::function<void(Frame&)> handler) = 0;
};
```

### Usage Example

```cpp
// Server side
auto factory = DuplexChannelFactory::getInstance();
auto server = factory.createInPlaceDuplexServer("image-proc", 
    BufferConfig(4096, 100*1024*1024));  // 100MB shared payload

server->start([](Frame& frame) {
    // Get mutable access to frame data
    uint8_t* data = frame.get_mutable_data();
    size_t size = frame.size();
    
    // Modify in place (e.g., apply image filter)
    apply_gaussian_blur(data, size);
    
    // No return needed - data modified in place
    // Server automatically preserves sequence number
});

// Client side
auto client = factory.createClient("image-proc");

// Send request
auto seq = client->sendRequest(image_data, image_size);

// Receive modified response (same memory, transformed)
auto response = client->receiveResponse(5000);
if (response.sequence() == seq) {
    // Process modified image
}
```

## Benefits

1. **Memory Efficiency**: 50% reduction in memory usage compared to dual-buffer approach
2. **True Zero-Copy**: Data never copied between request and response
3. **Performance**: Eliminates memory copy overhead for response creation
4. **Cache Locality**: Request and response data in same memory improves cache hits
5. **Backward Compatible**: Existing Reader/Writer classes unchanged

## Challenges and Solutions

### Challenge 1: Preventing Overwrite During Processing

**Problem**: Client might write new request while server processes previous one.

**Solution**: OIEB tracking ensures proper synchronization:
- Server's `read_count` in OIEB_Request prevents overwrite
- Client waits on `sem-space-available` before writing

### Challenge 2: Frame State Ambiguity

**Problem**: Is frame at position X a request or response?

**Solution**: Two OIEBs provide clear state:
- If `OIEB_Request.written > OIEB_Response.written`: It's a pending request
- If `OIEB_Response.written >= OIEB_Request.written`: It's a response

### Challenge 3: Wrap-Around Handling

**Problem**: In-place modification across wrap boundary.

**Solution**: Response inherits exact wrap behavior from request:
- If request wraps at position X, response wraps at same position
- Wrap marker shared between both views

## Implementation Phases

### Phase 1: SharedMemoryView Abstraction
- [ ] Implement IMemoryProvider interface
- [ ] Create SharedMemory and SharedMemoryView classes
- [ ] Update BufferConfig to use memory providers
- [ ] Modify Reader to support external memory

### Phase 2: Dual OIEB Support
- [ ] Extend shared memory layout for dual OIEB
- [ ] Implement OIEB_Request and OIEB_Response management
- [ ] Add four-semaphore synchronization

### Phase 3: InPlaceDuplexChannel
- [ ] Implement InPlaceDuplexChannel class
- [ ] Add IInPlaceDuplexServer interface
- [ ] Extend factory pattern
- [ ] Create comprehensive tests

### Phase 4: Cross-Platform Support
- [ ] C++ implementation
- [ ] C# implementation with P/Invoke considerations
- [ ] Python implementation with ctypes/cffi

## Testing Strategy

1. **Unit Tests**
   - SharedMemoryView mapping correctness
   - OIEB synchronization
   - Wrap-around behavior

2. **Integration Tests**
   - Request/response round trips
   - Multiple concurrent requests
   - Large payload handling

3. **Performance Benchmarks**
   - Compare with dual-buffer DuplexChannel
   - Measure memory usage reduction
   - Latency improvements

4. **Cross-Platform Tests**
   - C++ server with C# client
   - Python server with C++ client
   - All language combinations

## Backwards Compatibility

This proposal maintains full backward compatibility:
- Existing Reader/Writer APIs unchanged
- Current DuplexChannel continues working
- InPlaceDuplexChannel is opt-in via factory method
- Shared memory protocol remains compatible

## Security Considerations

In-place modification introduces additional concerns:
- Both processes access same memory
- Malicious server could corrupt requests
- No isolation between request/response data

Recommendation: Use only in trusted environments where both client and server are controlled.

## Conclusion

The proposed in-place duplex protocol with shared payload offers significant memory and performance benefits for request/response patterns where data is modified rather than replaced. By reusing the existing Reader/Writer infrastructure with a SharedMemoryView abstraction, we can implement this efficiently while maintaining backward compatibility.

The approach is particularly valuable for:
- Image/video processing pipelines
- Data transformation services
- High-throughput streaming with modifications
- Memory-constrained environments

## Appendix: Memory Layout Details

### Detailed Memory Map

```
Offset    Size     Description
------    ----     -----------
0x0000    88       OIEB_Request
0x0058    88       OIEB_Response  
0x00B0    varies   Metadata_Request
0x????    varies   Metadata_Response
0x????    varies   Shared Payload Buffer
```

### OIEB Field Usage in Shared Mode

| Field | OIEB_Request | OIEB_Response |
|-------|--------------|---------------|
| payload_size | Total buffer size | Same as Request |
| payload_free_bytes | Synchronized value | Synchronized value |
| payload_written_bytes | Bytes written as requests | Bytes modified as responses |
| payload_written_count | Request count | Response count |
| payload_read_count | Requests read by server | Responses read by client |
| writer_pid | Client PID | Server PID |
| reader_pid | Server PID | Client PID |

### Sequence Number Preservation

The frame sequence number is preserved throughout:
1. Client assigns sequence N to request
2. Server processes request with sequence N
3. Server marks response ready with same sequence N
4. Client correlates response using sequence N

This enables out-of-order processing while maintaining request/response correlation.