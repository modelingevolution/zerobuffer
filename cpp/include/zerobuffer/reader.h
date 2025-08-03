#ifndef ZEROBUFFER_READER_H
#define ZEROBUFFER_READER_H

#include "zerobuffer/types.h"
#include "zerobuffer/platform.h"
#include <memory>
#include <vector>
#include <stdexcept>

namespace zerobuffer {

class Reader {
public:
    // Create a new buffer and prepare for reading
    Reader(const std::string& name, const BufferConfig& config);
    
    // Destructor
    ~Reader();
    
    // Non-copyable
    Reader(const Reader&) = delete;
    Reader& operator=(const Reader&) = delete;
    
    // Move operations
    Reader(Reader&&) noexcept;
    Reader& operator=(Reader&&) noexcept;
    
    // Get buffer name
    const std::string& name() const { return name_; }
    
    // Get metadata (returns empty if no metadata written yet)
    std::vector<uint8_t> get_metadata() const;
    
    // Get metadata as raw pointer (zero-copy) - returns nullptr if no metadata
    const void* get_metadata_raw() const;
    
    // Get metadata as typed pointer (zero-copy)
    template<typename T>
    const T* get_metadata_as() const {
        size_t size = get_metadata_size();
        if (size < sizeof(T)) return nullptr;
        return static_cast<const T*>(get_metadata_raw());
    }
    
    // Get metadata size
    size_t get_metadata_size() const;
    
    // Wait for and read next frame
    Frame read_frame();
    
    // Signal that frame has been processed (frees buffer space)
    void release_frame(const Frame& frame);
    
    // Check if writer is connected
    bool is_writer_connected() const;
    
    // Get buffer statistics
    uint64_t frames_read() const;
    uint64_t bytes_read() const;
    
private:
    class Impl;
    std::unique_ptr<Impl> impl_;
    std::string name_;
};

// Exceptions
class ZeroBufferException : public std::runtime_error {
public:
    explicit ZeroBufferException(const std::string& what) : std::runtime_error(what) {}
};

class WriterDeadException : public ZeroBufferException {
public:
    WriterDeadException() : ZeroBufferException("Writer process is dead") {}
};

class SequenceError : public ZeroBufferException {
public:
    SequenceError(uint64_t expected, uint64_t got) 
        : ZeroBufferException("Sequence error: expected " + std::to_string(expected) + 
                              ", got " + std::to_string(got)) {}
};

} // namespace zerobuffer

#endif // ZEROBUFFER_READER_H