#ifndef ZEROBUFFER_TYPES_H
#define ZEROBUFFER_TYPES_H

#include <cstdint>
#include <cstddef>
#include <memory>

namespace zerobuffer {

// Alignment requirement for all blocks
constexpr size_t BLOCK_ALIGNMENT = 64;

// Protocol version structure (4 bytes)
struct ProtocolVersion {
    uint8_t major;     // Major version (breaking changes)
    uint8_t minor;     // Minor version (new features, backward compatible)
    uint8_t patch;     // Patch version (bug fixes)
    uint8_t reserved;  // Reserved for future use (must be 0)
    
    constexpr ProtocolVersion(uint8_t maj = 1, uint8_t min = 0, uint8_t pat = 0) noexcept
        : major(maj), minor(min), patch(pat), reserved(0) {}
    
    bool is_compatible_with(const ProtocolVersion& other) const noexcept {
        return major == other.major;  // Same major version required
    }
};

static_assert(sizeof(ProtocolVersion) == 4, "ProtocolVersion must be 4 bytes");

// Operation Info Exchange Block structure
struct OIEB {
    uint32_t oieb_size;           // Total OIEB size (always 128 for v1.x.x)
    ProtocolVersion version;      // Protocol version (currently 1.0.0)
    
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
    
    // Reserved for future use
    uint64_t reserved[4];
};

static_assert(sizeof(OIEB) == 128, "OIEB must be 128 bytes");
static_assert(sizeof(OIEB) % BLOCK_ALIGNMENT == 0, "OIEB must be aligned");
static_assert(offsetof(OIEB, metadata_size) == 8, "metadata_size must be at offset 8");

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

// Forward declarations
class Reader;

// Frame reference for zero-copy access with RAII (move-only, zero allocations)
class Frame {
public:
    // Frame release info - what we need to release the frame
    struct ReleaseInfo {
        void* reader_impl;  // Raw pointer to Reader::Impl
        uint64_t frame_size;
        void (*release_fn)(void*, uint64_t);  // Function pointer to release callback
        
        void release() {
            if (reader_impl && release_fn) {
                release_fn(reader_impl, frame_size);
            }
        }
    };
    
    // Default constructor - creates invalid frame
    Frame() : data_(nullptr), size_(0), sequence_(0), release_info_{nullptr, 0, nullptr} {}
    
    // Move constructor
    Frame(Frame&& other) noexcept 
        : data_(other.data_), size_(other.size_), sequence_(other.sequence_),
          release_info_(other.release_info_) {
        // Clear the moved-from frame
        other.data_ = nullptr;
        other.size_ = 0;
        other.sequence_ = 0;
        other.release_info_ = {nullptr, 0, nullptr};
    }
    
    // Move assignment
    Frame& operator=(Frame&& other) noexcept {
        if (this != &other) {
            // Release current frame if valid
            if (release_info_.reader_impl) {
                release_info_.release();
            }
            
            // Move from other
            data_ = other.data_;
            size_ = other.size_;
            sequence_ = other.sequence_;
            release_info_ = other.release_info_;
            
            // Clear the moved-from frame
            other.data_ = nullptr;
            other.size_ = 0;
            other.sequence_ = 0;
            other.release_info_ = {nullptr, 0, nullptr};
        }
        return *this;
    }
    
    // Delete copy operations - Frame is move-only
    Frame(const Frame&) = delete;
    Frame& operator=(const Frame&) = delete;
    
    // Destructor - calls release if frame is valid
    ~Frame() {
        if (release_info_.reader_impl) {
            release_info_.release();
        }
    }
    
    // Accessors
    const void* data() const { return data_; }
    void* data() { return const_cast<void*>(data_); }  // For mutable access
    size_t size() const { return size_; }
    uint64_t sequence() const { return sequence_; }
    bool valid() const { return data_ != nullptr; }
    bool is_valid() const { return valid(); }  // Alias for consistency
    
private:
    friend class Reader;  // Reader creates frames
    
    // Private constructor used by Reader
    Frame(const void* data, size_t size, uint64_t seq, ReleaseInfo info)
        : data_(data), size_(size), sequence_(seq), release_info_(info) {}
    
    const void* data_;
    size_t size_;
    uint64_t sequence_;
    ReleaseInfo release_info_;  // Zero allocations - just POD data
};

} // namespace zerobuffer

#endif // ZEROBUFFER_TYPES_H