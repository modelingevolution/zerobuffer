# ZeroBuffer API Documentation

**Language Requirements:**
- C++: Requires C++20 (for std::span and other modern features)
- C#: .NET 6.0 or later
- Python: 3.8 or later

## Table of Contents
1. [Core Principles](#core-principles)
2. [Basic API Usage](#basic-api-usage)
3. [Exceptions and Error Handling](#exceptions-and-error-handling)
4. [CLI Commands](#cli-commands)
5. [Typical Scenarios](#typical-scenarios)
6. [Duplex Channel Design](#duplex-channel-design)
7. [Implementation Roadmap](#implementation-roadmap)

## Core Principles

### One Consistent Interface
**All languages must have identical APIs** - No compatibility layers, no language-specific options, no ambiguity.

### Design Decisions

1. **Reader Creates Buffer, Writer Connects**
   - Reader always creates and owns the buffer
   - Writer always connects to an existing buffer
   - This is consistent across ALL implementations (C++, C#, Python)

2. **Consistent Defaults**
   - Buffer size: 256MB (268,435,456 bytes)
   - Metadata size: 4096 bytes
   - Frame size: 1024 bytes
   - Timeout: 5000ms
   - Frames: Writer=1000, Reader=0 (unlimited), Relay=0 (unlimited)

3. **No Compatibility Options**
   - No "ignored for compatibility" options
   - No language-specific options
   - If an option exists, it must work the same way everywhere

## Basic API Usage

### C++ API

```cpp
#include <zerobuffer/zerobuffer.h>

// Reader (creates buffer)
zerobuffer::BufferConfig config(4096, 256*1024*1024); // 4KB metadata, 256MB payload
zerobuffer::Reader reader("buffer-name", config);

// Wait for writer
if (!reader.is_writer_connected(30000)) { // 30 second timeout
    // Handle timeout
}

// Read frames
while (true) {
    zerobuffer::Frame frame = reader.read_frame();
    if (!frame.valid()) break;
    
    // Process frame.data(), frame.size()
    reader.release_frame(frame);
}

// Writer (connects to buffer)
zerobuffer::Writer writer("buffer-name");

// Write frames
std::vector<uint8_t> data(1024);
writer.write_frame(data.data(), data.size());
```

### C# API

```csharp
using ZeroBuffer;

// Reader (creates buffer)
var config = new BufferConfig(4096, 256*1024*1024);
using var reader = new Reader("buffer-name", config);

// Wait for writer
if (!reader.IsWriterConnected(30000)) { // 30 second timeout
    // Handle timeout
}

// Read frames
while (true) {
    var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
    if (!frame.IsValid) break;
    
    // Process frame data
    byte[] data = frame.ToArray();
    // Or access directly: frame.Data, frame.Size
}

// Writer (connects to buffer)
using var writer = new Writer("buffer-name");

// Configure write timeout (default: 5 seconds)
writer.WriteTimeout = TimeSpan.FromSeconds(10);

// Write frames
byte[] data = new byte[1024];
writer.WriteFrame(data);
```

### Python API

```python
import zerobuffer

# Reader (creates buffer)
config = zerobuffer.BufferConfig(metadata_size=4096, payload_size=256*1024*1024)
reader = zerobuffer.Reader("buffer-name", config)

# Wait for writer
if not reader.is_writer_connected(timeout_ms=30000):
    # Handle timeout
    pass

# Read frames
while True:
    frame = reader.read_frame()
    if not frame.is_valid:
        break
    
    # Process frame data (zero-copy numpy array)
    data = frame.as_numpy()  # Returns numpy array view
    reader.release_frame(frame)

# Writer (connects to buffer)
writer = zerobuffer.Writer("buffer-name")

# Write frames
data = bytes(1024)
writer.write_frame(data)
```

## Exceptions and Error Handling

### C# Exceptions

- **BufferNotFoundException**: Thrown when trying to connect to a non-existent buffer
- **WriterAlreadyConnectedException**: Thrown when a writer is already connected to the buffer
- **ReaderAlreadyConnectedException**: Thrown when a reader is already connected to the buffer
- **BufferFullException**: Thrown when the writer cannot write because the buffer is full after the configured timeout
  - Default timeout: 5 seconds
  - Configurable via `Writer.WriteTimeout` property
  - Writer waits on sem-r semaphore for the configured duration
  - If space doesn't become available within timeout, BufferFullException is thrown
- **ReaderDeadException**: Thrown when the writer detects the reader process has died
- **WriterDeadException**: Thrown when the reader detects the writer process has died
- **FrameTooLargeException**: Thrown when attempting to write a frame larger than the buffer's payload size
  - Frame size calculation: Total size = 16 bytes (header) + data payload size
  - Example: A 100-byte data payload requires 116 bytes of buffer space
  - If (16 + data_size) > buffer.PayloadSize, FrameTooLargeException is thrown

### C++ Exceptions

- **buffer_not_found_exception**: Buffer doesn't exist
- **writer_already_connected_exception**: Writer already connected to buffer
- **reader_already_connected_exception**: Reader already connected to buffer
- **buffer_full_exception**: Buffer is full after configured timeout
  - Default timeout: 5 seconds
  - Configurable via writer constructor or setter method
  - Writer waits on sem-r semaphore for the configured duration
- **reader_dead_exception**: Reader process died
- **writer_dead_exception**: Writer process died
- **frame_too_large_exception**: Frame exceeds buffer size
  - Same calculation as C#: Total size = 16 bytes (header) + data payload size
- Some operations also return invalid values (e.g., `Frame::invalid()`) for non-critical errors

### Python Exceptions

- **BufferNotFoundError**: Buffer doesn't exist
- **WriterAlreadyConnectedError**: Writer already connected
- **ReaderAlreadyConnectedError**: Reader already connected  
- **BufferFullError**: Buffer is full after timeout
- **ReaderDeadError**: Reader process died
- **WriterDeadError**: Writer process died
- **FrameTooLargeError**: Frame exceeds buffer size
  - Same calculation: Total size = 16 bytes (header) + data payload size

## CLI Commands

### Commands Implementation Status

| Command | C++ | C# | Python | Notes |
|---------|-----|----|---------|---------|
| writer  | ✅  | ✅ | ✅      | Fully compatible |
| reader  | ✅  | ✅ | ✅      | Fully compatible |
| relay   | ❌  | ✅ | ✅      | C++ has specialized benchmark relay only |

### Required Options for All Implementations

#### Writer
- `-n, --frames` (default: 1000)
- `-s, --size` (default: 1024)
- `-m, --metadata` (optional)
- `--metadata-file` (optional)
- `--pattern` (default: sequential)
- `--delay-ms` (default: 0)
- `--batch-size` (default: 1)
- `--json-output`
- `-v, --verbose`
- `-h, --help`

#### Reader
- `-n, --frames` (default: 0 for unlimited)
- `-s, --size` (default: 1024)
- `--timeout-ms` (default: 5000)
- `--verify` (default: none)
- `--checksum`
- `--batch-size` (default: 1)
- `--json-output`
- `-v, --verbose`
- `-h, --help`

#### Relay
- `-n, --frames` (default: 0 for unlimited)
- `--create-output`
- `--buffer-size` (default: 256MB)
- `--timeout-ms` (default: 5000)
- `--transform` (default: none)
- `--xor-key` (default: 255)
- `--log-interval` (default: 100)
- `--json-output`
- `-v, --verbose`
- `-h, --help`

## Typical Scenarios

### Scenario 1: Simple Producer-Consumer

```
Process A (C++)          Process B (C#/Python)
   Writer      ------>      Reader
```

**C++ Producer:**
```cpp
zerobuffer::Writer writer("video-stream");
for (int i = 0; i < 1000; i++) {
    std::vector<uint8_t> frame = capture_frame();
    writer.write_frame(frame.data(), frame.size());
}
```

**C# Consumer:**
```csharp
var reader = new Reader("video-stream", new BufferConfig(4096, 256*1024*1024));
while (reader.IsWriterConnected()) {
    var frame = reader.ReadFrame(TimeSpan.FromSeconds(1));
    if (frame.IsValid) {
        ProcessFrame(frame.ToArray());
    }
}
```

### Scenario 2: Pipeline Processing

```
Camera (C++) --> Enhancement (Python) --> Display (C#)
   Writer           Reader/Writer          Reader
```

**Stage 1 - Camera (C++):**
```cpp
zerobuffer::Writer camera_out("raw-frames");
// Write raw frames...
```

**Stage 2 - Enhancement (Python):**
```python
reader = zerobuffer.Reader("raw-frames", config)
writer = zerobuffer.Writer("enhanced-frames")

while True:
    frame = reader.read_frame()
    if not frame.is_valid:
        break
    
    # Process frame
    enhanced = enhance_image(frame.as_numpy())
    writer.write_frame(enhanced.tobytes())
    reader.release_frame(frame)
```

**Stage 3 - Display (C#):**
```csharp
var reader = new Reader("enhanced-frames", config);
// Display frames...
```

### Scenario 3: Request-Response Pattern

```
Client (C++)  Request   Server (C#/Python)
   Writer    -------->    Reader
   Reader    <--------    Writer
             Response
```

This requires two separate buffers for bidirectional communication.

## Duplex Channel Design with Separation of Concerns

### Core Interfaces

#### IDuplexClient
Client-side interface for sending requests and receiving responses.

```cpp
// C++ Interface (requires C++20 for std::span)
class IDuplexClient {
public:
    virtual ~IDuplexClient() = default;
    
    // Send a request with data copy and return the sequence number for correlation
    // This method returns immediately after writing to the request buffer
    virtual uint64_t sendRequest(const void* data, size_t size) = 0;
    
    // Acquire buffer for zero-copy write. Returns sequence number and span to buffer.
    // Call commitRequest() after writing to send the request.
    virtual std::pair<uint64_t, std::span<uint8_t>> acquireRequestBuffer(size_t size) = 0;
    
    // Commit the request after writing to the acquired buffer
    virtual void commitRequest() = 0;
    
    // Receive a response frame. This method blocks until a response is available or timeout
    // The caller is responsible for correlating responses using the sequence number in the frame
    virtual Frame receiveResponse(int timeout_ms) = 0;
    
    // Check if server is connected to the request buffer
    virtual bool isServerConnected() const = 0;
};
```

```csharp
// C# Interface
public interface IDuplexClient : IDisposable
{
    // Send a request with data copy and return the sequence number for correlation
    // This method returns immediately after writing to the request buffer
    ulong SendRequest(byte[] data);
    
    // Acquire buffer for zero-copy write. Returns sequence number.
    // The buffer parameter will be set to a span pointing to the acquired buffer.
    // Call CommitRequest() after writing to send the request.
    ulong AcquireRequestBuffer(int size, out Span<byte> buffer);
    
    // Commit the request after writing to the acquired buffer
    void CommitRequest();
    
    // Receive a response. This method blocks until a response is available or timeout
    // Returns a DuplexResponse that provides access to sequence number and data
    DuplexResponse ReceiveResponse(TimeSpan timeout);
    
    // Check if server is connected to the request buffer
    bool IsServerConnected { get; }
}
```

```python
# Python Interface
class IDuplexClient(ABC):
    @abstractmethod
    def send_request(self, data: bytes) -> int:
        """Send a request with data copy and return the sequence number for correlation.
        This method returns immediately after writing to the request buffer."""
        pass
    
    @abstractmethod
    def acquire_request_buffer(self, size: int) -> Tuple[int, memoryview]:
        """Acquire buffer for zero-copy write. Returns (sequence_number, buffer).
        Call commit_request() after writing to send the request."""
        pass
    
    @abstractmethod
    def commit_request(self) -> None:
        """Commit the request after writing to the acquired buffer"""
        pass
    
    @abstractmethod
    def receive_response(self, timeout_ms: int) -> Frame:
        """Receive a response frame. This method blocks until a response is available or timeout.
        The caller is responsible for correlating responses using the sequence number in the frame."""
        pass
    
    @property
    @abstractmethod
    def is_server_connected(self) -> bool:
        """Check if server is connected to the request buffer"""
        pass
```

#### IDuplexServer
Base server-side interface with common functionality.

```cpp
// C++ Interface
class IDuplexServer {
public:
    virtual ~IDuplexServer() = default;
    
    // Stop processing
    virtual void stop() = 0;
    
    // Check if running
    virtual bool isRunning() const = 0;
};

// Handler function that returns response data as span (requires C++20)
using RequestHandler = std::function<std::span<const uint8_t>(const Frame&)>;

// Server that processes immutable requests and returns new response data
class IImmutableDuplexServer : public IDuplexServer {
public:
    // Start processing requests with a handler that returns response data as span
    // Note: is_async parameter is currently ignored in C++ implementation
    virtual void start(RequestHandler handler, bool is_async = false) = 0;
};

// Server that mutates request data in-place (zero-copy)
class IMutableDuplexServer : public IDuplexServer {
public:
    // Start processing with mutable handler
    // Note: is_async parameter is currently ignored in C++ implementation
    virtual void start(std::function<void(Frame&)> handler, bool is_async = false) = 0;
};
```

```csharp
// C# Interface
public interface IDuplexServer : IDisposable
{
    // Stop processing
    void Stop();
    
    // Check if running
    bool IsRunning { get; }
}

// Handler delegate that returns response data as ReadOnlySpan
public delegate ReadOnlySpan<byte> RequestHandler(Frame request);

// Server that processes immutable requests and returns new response data
public interface IImmutableDuplexServer : IDuplexServer
{
    // Start processing requests with a handler that returns response data as ReadOnlySpan
    // Note: isAsync parameter is currently ignored - server always runs in background thread
    void Start(RequestHandler handler, bool isAsync = false);
}

// Server that mutates request data in-place (zero-copy)
public interface IMutableDuplexServer : IDuplexServer
{
    // Start processing with mutable handler
    // Note: isAsync parameter is currently ignored - server always runs in background thread
    void Start(Action<Frame> handler, bool isAsync = false);
}
```

```python
# Python Interface
class IDuplexServer(ABC):
    @abstractmethod
    def stop(self) -> None:
        """Stop processing"""
        pass
    
    @property
    @abstractmethod
    def is_running(self) -> bool:
        """Check if running"""
        pass

# Processing mode enum
class ProcessingMode(Enum):
    SINGLE_THREAD = "single_thread"  # Process requests sequentially in one background thread
    THREAD_POOL = "thread_pool"      # Process each request in a thread pool (not yet implemented)

# Server that processes immutable requests and returns new response data
class IImmutableDuplexServer(IDuplexServer):
    @abstractmethod
    def start(self, handler: Callable[[Frame], bytes], mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """Start processing requests with a handler that returns new data"""
        pass
    
    @abstractmethod
    async def start_async(self, handler: Callable[[Frame], Awaitable[bytes]]) -> None:
        """Start processing asynchronously"""
        pass

# Server that mutates request data in-place (zero-copy)
class IMutableDuplexServer(IDuplexServer):
    @abstractmethod
    def start(self, handler: Callable[[Frame], None], mode: ProcessingMode = ProcessingMode.SINGLE_THREAD) -> None:
        """Start processing with mutable handler"""
        pass
```

### Factory Pattern

```cpp
// C++ Factory
class IDuplexChannelFactory {
public:
    virtual ~IDuplexChannelFactory() = default;
    
    // Create an immutable server (processes immutable requests, returns new response data)
    virtual std::unique_ptr<IImmutableDuplexServer> createImmutableServer(
        const std::string& channel_name,
        const BufferConfig& config) = 0;
    
    // Create a mutable server (mutates request data in-place)
    virtual std::unique_ptr<IMutableDuplexServer> createMutableServer(
        const std::string& channel_name,
        const BufferConfig& config) = 0;
    
    // Connect to existing duplex channel (client-side)
    virtual std::unique_ptr<IDuplexClient> createClient(
        const std::string& channel_name) = 0;
    
    // Get factory instance
    static IDuplexChannelFactory& getInstance();
};
```

```csharp
// C# Factory
public interface IDuplexChannelFactory
{
    // Create an immutable server (processes immutable requests, returns new response data)
    IImmutableDuplexServer CreateImmutableServer(string channelName, BufferConfig config);
    
    // Create a mutable server (mutates request data in-place)
    IMutableDuplexServer CreateMutableServer(string channelName, BufferConfig config);
    
    // Connect to existing duplex channel (client-side)
    IDuplexClient CreateClient(string channelName);
}

public class DuplexChannelFactory : IDuplexChannelFactory
{
    private readonly ILoggerFactory _loggerFactory;
    
    // Constructor with optional logger factory for DI
    public DuplexChannelFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }
    
    public IImmutableDuplexServer CreateImmutableServer(string channelName, BufferConfig config)
    {
        var logger = _loggerFactory.CreateLogger<ImmutableDuplexServer>();
        return new ImmutableDuplexServer(channelName, config, logger);
    }
    
    public IMutableDuplexServer CreateMutableServer(string channelName, BufferConfig config)
    {
        var logger = _loggerFactory.CreateLogger<MutableDuplexServer>();
        return new MutableDuplexServer(channelName, config, logger);
    }
    
    public IDuplexClient CreateClient(string channelName) 
        => new DuplexClient(channelName);
}
```

```python
# Python Factory
class IDuplexChannelFactory(ABC):
    @abstractmethod
    def create_immutable_server(self, channel_name: str, config: BufferConfig) -> IImmutableDuplexServer:
        """Create an immutable server (processes immutable requests, returns new response data)"""
        pass
    
    @abstractmethod
    def create_mutable_server(self, channel_name: str, config: BufferConfig) -> IMutableDuplexServer:
        """Create a mutable server (mutates request data in-place)"""
        pass
    
    @abstractmethod
    def create_client(self, channel_name: str) -> IDuplexClient:
        """Connect to existing duplex channel (client-side)"""
        pass

class DuplexChannelFactory(IDuplexChannelFactory):
    _instance = None
    
    @classmethod
    def get_instance(cls):
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance
    
    def create_immutable_server(self, channel_name: str, config: BufferConfig) -> IImmutableDuplexServer:
        return ImmutableDuplexServer(channel_name, config)
    
    def create_mutable_server(self, channel_name: str, config: BufferConfig) -> IMutableDuplexServer:
        return MutableDuplexServer(channel_name, config)
    
    def create_client(self, channel_name: str) -> IDuplexClient:
        return DuplexClient(channel_name)
```

### Usage Examples

#### Example 1: Immutable Request-Response

**Server Side (C++):**
```cpp
auto factory = IDuplexChannelFactory::getInstance();
auto server = factory.createImmutableServer("image-processing", BufferConfig(4096, 256*1024*1024));

// Process requests and return new response data
server->start([](const Frame& request) -> std::vector<uint8_t> {
    // Server automatically preserves request.Sequence in the response
    auto processed = processImage(request.data(), request.size());
    return processed;
});
```

**Client Side (C#):**
```csharp
var factory = DuplexChannelFactory.Instance;
var client = factory.CreateClient("image-processing");

byte[] imageData = File.ReadAllBytes("input.jpg");

// Option 1: Send with copy
ulong sequenceNumber = client.SendRequest(imageData);

// Option 2: Zero-copy write (no allocations)
ulong sequenceNumber2 = client.AcquireRequestBuffer(imageData.Length, out Span<byte> buffer);
imageData.CopyTo(buffer);
client.CommitRequest();

// Receive response (could be on different thread)
var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));

// Check if this is our response by matching sequence number
if (response.IsValid && response.Sequence == sequenceNumber)
{
    File.WriteAllBytes("output.jpg", response.ToArray());
}
```

#### Example 2: Python Immutable Server

**Server Side (Python):**
```python
from zerobuffer.duplex import DuplexChannelFactory, ProcessingMode
from zerobuffer import BufferConfig

factory = DuplexChannelFactory()
# Create immutable server
server = factory.create_immutable_server("image-processing", BufferConfig(4096, 256*1024*1024))

def process_image(frame):
    """Process image and return new data"""
    # Frame is automatically disposed via context manager (RAII)
    # Server automatically preserves frame.sequence in the response
    
    # Process the image data
    data = bytes(frame.data)  # Access frame data
    processed = apply_filters(data)  # Your processing logic
    
    return processed  # Return new response data

# Start server with single-thread processing
server.start(process_image, ProcessingMode.SINGLE_THREAD)
```

**Client Side (Python):**
```python
from zerobuffer.duplex import DuplexChannelFactory

factory = DuplexChannelFactory()
client = factory.create_client("image-processing")

# Send request
image_data = load_image("input.jpg")
sequence = client.send_request(image_data)

# Receive response
response = client.receive_response(timeout_ms=5000)

# Verify response matches our request
if response.is_valid and response.sequence == sequence:
    # Use context manager for RAII - frame is disposed on exit
    with response:
        save_image("output.jpg", bytes(response.data))
```

#### Example 3: Mutable Zero-Copy Processing (Future - v2.0.0)

**Note:** Mutable servers with true zero-copy processing are planned for v2.0.0

**Client Side (C++):**
```cpp
auto client = factory.createClient("filters");
std::vector<uint8_t> image = loadImage();

// Send request and get sequence number
uint64_t sequence_number = client->sendRequest(image.data(), image.size());

// Receive response
auto response = client->receiveResponse(5000);

// Check if this is our response by matching sequence number
if (response.valid() && response.sequence() == sequence_number) {
    saveImage(response);
}
```

### Duplex Channel Protocol

#### Request/Response Correlation
The duplex channel uses the existing Frame sequence numbers for request/response correlation:

1. **Client sends request**: Writes data to request buffer, Writer assigns sequence number
2. **Server processes**: Reads frame with sequence number N, processes it
3. **Server sends response**: Writes response data with THE SAME sequence number N
4. **Client receives**: Reads response frame, checks if sequence matches the request

#### Protocol Details
- **No additional headers**: Uses existing ZeroBuffer Frame structure
- **Sequence preservation**: Server MUST preserve the request's sequence number in response
- **Zero overhead**: No extra bytes beyond standard Frame header
- **Simple correlation**: `if (response.Sequence == requestSequence) { /* matched */ }`

### Implementation Notes

- **Channel Naming**: Request channel: `{channel_name}_request`, Response channel: `{channel_name}_response`
- **Threading**: Clients are thread-safe; servers process one request at a time
- **Error Handling**: Timeouts return invalid frames; disconnections throw exceptions
- **Async Support**: Not initially implemented due to Frame being a ref struct. Would require async semaphores with custom awaiters
- **Independent Operations**: Send and receive can be called from different threads, enabling true duplex communication
- **Zero-Copy Mutable Processing**: C# implementation supports true zero-copy via Frame.GetMutableSpan()
- **1-to-1 Communication**: ZeroBuffer enforces single reader/writer per buffer - no concurrent clients allowed

### Benefits of This Design

1. **Clear Separation of Concerns**: Client and server interfaces are completely separate
2. **Single Responsibility**: Each server type has one clear purpose (immutable vs mutable)
3. **Type Safety**: Compile-time enforcement of correct handler signatures
4. **Flexibility**: Easy to add new server types or optimization strategies
5. **Testability**: Each interface can be mocked independently
6. **Language Consistency**: Same patterns across C++, C#, and Python

## Implementation Roadmap

### Phase 1: API Stabilization (Current)
- [x] Unify CLI interfaces across languages
- [x] Standardize options and defaults
- [x] Document basic API usage
- [ ] Add Python bindings with basic Reader/Writer

### Phase 2: Duplex Channel - Basic Implementation
- [x] Implement basic duplex channel with two separate buffers (C++ ✅, C# ✅, Python ✅)
- [x] Add request-response correlation (sequence numbers) (C++ ✅, C# ✅, Python ✅)
- [x] Implement timeout handling (C++ ✅, C# ✅, Python ✅)
- [x] Add comprehensive tests (C++ ✅, C# ✅, Python ✅)
- [x] Implement Python duplex channel with RAII Frame support

### Phase 3: Zero-Copy Optimization
- [ ] Design shared payload space protocol
- [ ] Modify Reader/Writer constructors to support:
  - `Reader(name, config, allow_shared_payload=true)`
  - `Writer(name, reuse_reader_payload=true)`
- [ ] Implement reference counting for shared payloads
- [ ] Add safety mechanisms to prevent use-after-free

### Phase 4: Advanced Features
- [ ] Request cancellation
- [ ] Performance optimizations

## Constructor Modifications for Payload Reuse

### Current Constructors

**C++ Reader:**
```cpp
Reader(const std::string& name, const BufferConfig& config);
```

**C++ Writer:**
```cpp
Writer(const std::string& name);
```

### Proposed Modifications

**C++ Reader (Extended):**
```cpp
Reader(const std::string& name, const BufferConfig& config, 
       bool allow_payload_sharing = false);
```

**C++ Writer (Extended):**
```cpp
// Connect and potentially share reader's payload space
Writer(const std::string& name, bool request_payload_sharing = false);
```

**New Shared Buffer Creation:**
```cpp
// Create a buffer that explicitly shares payload with another
SharedWriter(const std::string& name, Reader& source_reader);
```

### Safety Considerations

1. **Lifetime Management**: Shared payload must outlive all users
2. **Synchronization**: Additional semaphores for coordinating shared access
3. **Metadata**: Track sharing status in OIEB structure
4. **Fallback**: Gracefully fall back to copying if sharing fails

### Protocol Extension

Extend OIEB structure to support payload sharing:
```cpp
struct OIEB {
    // ... existing fields ...
    
    // Payload sharing fields
    uint64_t payload_sharing_enabled;  // 1 if sharing is allowed
    uint64_t payload_share_count;      // Number of active shares
    uint64_t payload_owner_pid;        // PID of process that owns payload
    uint64_t reserved_sharing[5];      // Reserved for future use
};
```

This design allows for future zero-copy optimizations while maintaining backward compatibility with existing code.