#ifndef ZEROBUFFER_WRITER_H
#define ZEROBUFFER_WRITER_H

#include "zerobuffer/types.h"
#include "zerobuffer/platform.h"
#include "zerobuffer/reader.h"  // For ZeroBufferException
#include <memory>
#include <vector>

namespace zerobuffer {

class Writer {
public:
    // Connect to existing buffer
    explicit Writer(const std::string& name);
    
    // Destructor
    ~Writer();
    
    // Non-copyable
    Writer(const Writer&) = delete;
    Writer& operator=(const Writer&) = delete;
    
    // Move operations
    Writer(Writer&&) noexcept;
    Writer& operator=(Writer&&) noexcept;
    
    // Set metadata (can only be called once)
    void set_metadata(const void* data, size_t size);
    void set_metadata(const std::vector<uint8_t>& data);
    
    // Zero-copy metadata access
    void* get_metadata_buffer(size_t size);
    void commit_metadata();
    
    // Write frame
    void write_frame(const void* data, size_t size);
    void write_frame(const std::vector<uint8_t>& data);
    
    // Zero-copy frame access
    void* get_frame_buffer(size_t size, uint64_t& sequence_number);
    void commit_frame();
    
    // Check if reader is connected
    bool is_reader_connected() const;
    
    // Get buffer statistics
    uint64_t frames_written() const;
    uint64_t bytes_written() const;
    
    // Get buffer name
    const std::string& name() const { return name_; }
    
private:
    class Impl;
    std::unique_ptr<Impl> impl_;
    std::string name_;
};

// Exceptions
class ReaderDeadException : public ZeroBufferException {
public:
    ReaderDeadException() : ZeroBufferException("Reader process is dead") {}
};

class MetadataAlreadyWrittenException : public ZeroBufferException {
public:
    MetadataAlreadyWrittenException() : ZeroBufferException("Metadata has already been written") {}
};

class BufferFullException : public ZeroBufferException {
public:
    BufferFullException() : ZeroBufferException("Buffer is full") {}
};

class InvalidFrameSizeException : public ZeroBufferException {
public:
    InvalidFrameSizeException() : ZeroBufferException("Invalid frame size (zero or too large)") {}
};

class FrameTooLargeException : public ZeroBufferException {
public:
    FrameTooLargeException() : ZeroBufferException("Frame size exceeds buffer capacity") {}
};

} // namespace zerobuffer

#endif // ZEROBUFFER_WRITER_H