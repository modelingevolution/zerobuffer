# Changelog

All notable changes to the ZeroBuffer project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2024-08-15

### Added
- Python Duplex Channel implementation with full v1.0.0 protocol support
  - `ImmutableDuplexServer` for request-response processing
  - `DuplexClient` for sending requests and receiving correlated responses
  - RAII Frame management using Python context managers
  - Full cross-platform compatibility with C++ and C# implementations
  - Test scenario 13.1 passing for all platform combinations

### Changed
- Updated Python Frame to support disposal callbacks for proper semaphore signaling
- Enhanced Python OIEB structure to include ProtocolVersion for v1.0.0 compliance

### Fixed
- Python Frame disposal now correctly signals semaphore after frame processing
- Python protocol version structure now matches C++/C# implementations exactly

## [1.0.0] - 2024-01-15

### Added
- Initial release with full protocol v1.0.0 support
- C++ implementation with zero-copy shared memory IPC
- C# implementation with .NET 6+ support
- Python implementation with multiprocessing.shared_memory
- Cross-platform compatibility (Linux, Windows, macOS)
- Basic Reader/Writer API for streaming data
- Duplex Channel support for request-response patterns (C++, C#)
- Comprehensive test suite with Harmony framework
- Protocol documentation and API specifications

### Core Features
- Zero-copy inter-process communication
- Ring buffer with wrap-around handling
- Semaphore-based synchronization
- Process liveness detection via PID monitoring
- Metadata support for schema exchange
- Frame-based data transfer with sequence numbers

### Performance
- Sub-millisecond latency for local IPC
- Throughput limited only by memory bandwidth
- Minimal CPU overhead with semaphore synchronization
- Zero memory allocations in hot path (C++, C#)

### Known Limitations
- Single reader, single writer per buffer
- No built-in encryption (designed for trusted local IPC)
- Python cannot achieve true zero-copy writes due to GIL
- Maximum buffer size limited by available shared memory

## Version History

### Protocol Versions
- **v1.0.0** - Current stable protocol
  - 128-byte OIEB structure
  - Single buffer architecture
  - Request/response correlation via sequence numbers

### Future Plans (v2.0.0)
- Shared payload buffers for duplex channels
- Mutable server support with true zero-copy
- External shared memory mapping
- 1024-byte OIEB for extended metadata
- Multi-reader support (pub-sub pattern)
- Async/await support for all languages