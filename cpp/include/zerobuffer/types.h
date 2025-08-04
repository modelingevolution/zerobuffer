#ifndef ZEROBUFFER_TYPES_H
#define ZEROBUFFER_TYPES_H

#include <cstdint>
#include <cstddef>

namespace zerobuffer {

// Alignment requirement for all blocks
constexpr size_t BLOCK_ALIGNMENT = 64;

// Operation Info Exchange Block structure
struct OIEB {
    uint64_t operation_size;      // Total OIEB size
    
    uint64_t metadata_size;       // Total metadata block size
    uint64_t metadata_free_bytes; // Free bytes in metadata block
    uint64_t metadata_written_bytes; // Written bytes in metadata block
    
    uint64_t payload_size;        // Total payload block size
    uint64_t payload_free_bytes;  // Free bytes in payload block
    uint64_t payload_write_pos;   // Current write position in buffer
    uint64_t payload_read_pos;    // Current read position in buffer
    uint64_t payload_written_count; // Number of frames written
    uint64_t payload_read_count;   // Number of frames read
    
    uint64_t writer_pid;          // Writer process ID (0 if none)
    uint64_t reader_pid;          // Reader process ID (0 if none)
    
    // Padding to ensure 64-byte alignment
    uint64_t reserved[4];
};

static_assert(sizeof(OIEB) == 128, "OIEB must be 128 bytes");
static_assert(sizeof(OIEB) % BLOCK_ALIGNMENT == 0, "OIEB must be aligned");

// Frame header structure
struct FrameHeader {
    uint64_t payload_size;        // Size of the frame data
    uint64_t sequence_number;     // Sequence number
};

// Configuration for creating a buffer
struct BufferConfig {
    size_t metadata_size;         // Size of metadata block
    size_t payload_size;          // Size of payload block
    
    BufferConfig(size_t meta = 1024, size_t payload = 1024 * 1024)
        : metadata_size(meta), payload_size(payload) {}
};

// Frame reference for zero-copy access
class Frame {
public:
    Frame() : data_(nullptr), size_(0), sequence_(0) {}
    Frame(const void* data, size_t size, uint64_t seq)
        : data_(data), size_(size), sequence_(seq) {}
    Frame(void* data, size_t size, uint64_t seq)
        : data_(data), size_(size), sequence_(seq) {}
    
    const void* data() const { return data_; }
    void* data() { return const_cast<void*>(data_); }  // For mutable access
    size_t size() const { return size_; }
    uint64_t sequence() const { return sequence_; }
    bool valid() const { return data_ != nullptr; }
    bool is_valid() const { return valid(); }  // Alias for consistency
    
private:
    const void* data_;
    size_t size_;
    uint64_t sequence_;
};

} // namespace zerobuffer

#endif // ZEROBUFFER_TYPES_H