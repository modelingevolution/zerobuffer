#include "zerobuffer/writer.h"
#include <cstring>
#include <atomic>

namespace zerobuffer {

class Writer::Impl {
public:
    explicit Impl(const std::string& name) 
        : name_(name), sequence_number_(1), frames_written_(0), bytes_written_(0), 
          metadata_written_(false), pending_metadata_size_(0), pending_frame_size_(0),
          pending_frame_sequence_(0), pending_frame_total_size_(0), pending_write_pos_(0) {
        
        // Open existing shared memory
        shm_ = SharedMemory::open(name);
        
        // Verify OIEB
        OIEB* oieb = get_oieb();
        if (oieb->operation_size != sizeof(OIEB)) {
            throw ZeroBufferException("Invalid OIEB size - version mismatch?");
        }
        
        // Check if reader exists
        if (oieb->reader_pid == 0 || !platform::process_exists(oieb->reader_pid)) {
            throw ZeroBufferException("No active reader found");
        }
        
        // Check if another writer exists
        if (oieb->writer_pid != 0 && platform::process_exists(oieb->writer_pid)) {
            throw ZeroBufferException("Another writer is already connected");
        }
        
        // Set writer PID
        oieb->writer_pid = platform::get_current_pid();
        
        // Open semaphores
        sem_write_ = Semaphore::open("/sem-w-" + name);
        sem_read_ = Semaphore::open("/sem-r-" + name);
        
        // Set up pointers
        metadata_start_ = reinterpret_cast<uint8_t*>(shm_->data()) + sizeof(OIEB);
        payload_start_ = metadata_start_ + oieb->metadata_size;
        
        // Check if metadata was already written
        metadata_written_ = (oieb->metadata_written_bytes > 0);
    }
    
    ~Impl() {
        // Clear writer PID
        if (shm_ && shm_->valid()) {
            OIEB* oieb = get_oieb();
            oieb->writer_pid = 0;
        }
    }
    
    OIEB* get_oieb() {
        return reinterpret_cast<OIEB*>(shm_->data());
    }
    
    const OIEB* get_oieb() const {
        return reinterpret_cast<const OIEB*>(shm_->data());
    }
    
    void* get_metadata_buffer(size_t size) {
        if (metadata_written_) {
            throw MetadataAlreadyWrittenException();
        }
        
        OIEB* oieb = get_oieb();
        
        // Check size with header
        size_t total_size = sizeof(uint64_t) + size;
        if (total_size > oieb->metadata_size) {
            throw ZeroBufferException("Metadata too large for buffer");
        }
        
        // Store pending size
        pending_metadata_size_ = size;
        
        // Return pointer to data area (after size prefix)
        return metadata_start_ + sizeof(uint64_t);
    }
    
    void commit_metadata() {
        if (metadata_written_) {
            throw MetadataAlreadyWrittenException();
        }
        
        OIEB* oieb = get_oieb();
        
        // Write size prefix
        uint64_t meta_size = pending_metadata_size_;
        std::memcpy(metadata_start_, &meta_size, sizeof(meta_size));
        
        // Update OIEB
        size_t total_size = sizeof(uint64_t) + pending_metadata_size_;
        oieb->metadata_written_bytes = total_size;
        oieb->metadata_free_bytes = oieb->metadata_size - total_size;
        
        metadata_written_ = true;
    }
    
    void set_metadata(const void* data, size_t size) {
        if (metadata_written_) {
            throw MetadataAlreadyWrittenException();
        }
        
        OIEB* oieb = get_oieb();
        
        // Check size with header
        size_t total_size = sizeof(uint64_t) + size;
        if (total_size > oieb->metadata_size) {
            throw ZeroBufferException("Metadata too large for buffer");
        }
        
        // Write size prefix
        uint64_t meta_size = size;
        std::memcpy(metadata_start_, &meta_size, sizeof(meta_size));
        
        // Write metadata
        if (size > 0) {
            std::memcpy(metadata_start_ + sizeof(meta_size), data, size);
        }
        
        // Update OIEB
        oieb->metadata_written_bytes = total_size;
        oieb->metadata_free_bytes = oieb->metadata_size - total_size;
        
        metadata_written_ = true;
    }
    
    size_t get_continuous_free_space() const {
        const OIEB* oieb = get_oieb();
        
        // Calculate continuous free space based on read/write positions
        if (oieb->payload_write_pos >= oieb->payload_read_pos) {
            // Write ahead of read - check space to end and beginning
            size_t space_to_end = oieb->payload_size - oieb->payload_write_pos;
            if (oieb->payload_read_pos == 0) {
                // Can't wrap if reader at beginning
                return space_to_end;
            }
            // Can use space at beginning if we wrap
            return std::max(space_to_end, oieb->payload_read_pos);
        } else {
            // Read ahead of write - continuous space until read pos
            return oieb->payload_read_pos - oieb->payload_write_pos;
        }
    }
    
    void write_frame(const void* data, size_t size) {
        if (size == 0) {
            throw InvalidFrameSizeException();
        }
        
        size_t total_size = sizeof(FrameHeader) + size;
        
        while (true) {
            OIEB* oieb = get_oieb();
            
            // Check if reader is still alive
            if (!is_reader_connected()) {
                throw ReaderDeadException();
            }
            
            // Check continuous free space
            size_t continuous_free = get_continuous_free_space();
            
            if (continuous_free >= total_size) {
                // We have enough space
                break;
            }
            
            // Wait for reader to free space
            if (!sem_read_->wait(std::chrono::milliseconds(5000))) {
                // Timeout - check if reader is alive
                if (!is_reader_connected()) {
                    throw ReaderDeadException();
                }
                // Continue waiting
            }
        }
        
        OIEB* oieb = get_oieb();
        
        // Check if we need to wrap
        size_t space_to_end = oieb->payload_size - oieb->payload_write_pos;
        if (space_to_end < total_size && oieb->payload_read_pos > 0) {
            // Need to wrap to beginning
            // Write a special marker at current position if there's space for a header
            if (space_to_end >= sizeof(FrameHeader)) {
                uint8_t* wrap_marker_ptr = payload_start_ + oieb->payload_write_pos;
                // Write wrap marker header
                FrameHeader wrap_marker;
                wrap_marker.payload_size = 0;  // Indicates wrap-around
                wrap_marker.sequence_number = 0;
                std::memcpy(wrap_marker_ptr, &wrap_marker, sizeof(wrap_marker));
                
                // Increment written count for the wrap marker
                oieb->payload_written_count++;
            }
            // Account for the wasted space at the end
            oieb->payload_free_bytes -= space_to_end;
            oieb->payload_write_pos = 0;
        }
        
        // Write frame header
        uint8_t* write_ptr = payload_start_ + oieb->payload_write_pos;
        FrameHeader header;
        header.payload_size = size;
        header.sequence_number = sequence_number_;
        std::memcpy(write_ptr, &header, sizeof(header));
        
        // Write frame data
        std::memcpy(write_ptr + sizeof(header), data, size);
        
        // Update tracking
        oieb->payload_write_pos += total_size;
        sequence_number_++;
        frames_written_++;
        bytes_written_ += size;
        
        // Update OIEB
        oieb->payload_free_bytes -= total_size;
        oieb->payload_written_count++;
        
        // Release memory barrier
        std::atomic_thread_fence(std::memory_order_release);
        
        // Signal reader
        sem_write_->signal();
    }
    
    void* get_frame_buffer(size_t size, uint64_t& sequence_number) {
        if (size == 0) {
            throw InvalidFrameSizeException();
        }
        
        size_t total_size = sizeof(FrameHeader) + size;
        
        while (true) {
            OIEB* oieb = get_oieb();
            
            // Check if reader is still alive
            if (!is_reader_connected()) {
                throw ReaderDeadException();
            }
            
            // Check continuous free space
            size_t continuous_free = get_continuous_free_space();
            
            if (continuous_free >= total_size) {
                // We have enough space
                break;
            }
            
            // Wait for reader to free space
            if (!sem_read_->wait(std::chrono::milliseconds(5000))) {
                // Timeout - check if reader is alive
                if (!is_reader_connected()) {
                    throw ReaderDeadException();
                }
                // Continue waiting
            }
        }
        
        OIEB* oieb = get_oieb();
        
        // Check if we need to wrap
        size_t space_to_end = oieb->payload_size - oieb->payload_write_pos;
        if (space_to_end < total_size && oieb->payload_read_pos > 0) {
            // Need to wrap to beginning
            // Write a special marker at current position if there's space for a header
            if (space_to_end >= sizeof(FrameHeader)) {
                uint8_t* wrap_marker_ptr = payload_start_ + oieb->payload_write_pos;
                // Write wrap marker header
                FrameHeader wrap_marker;
                wrap_marker.payload_size = 0;  // Indicates wrap-around
                wrap_marker.sequence_number = 0;
                std::memcpy(wrap_marker_ptr, &wrap_marker, sizeof(wrap_marker));
                
                // Increment written count for the wrap marker
                oieb->payload_written_count++;
            }
            // Account for the wasted space at the end
            oieb->payload_free_bytes -= space_to_end;
            oieb->payload_write_pos = 0;
        }
        
        // Store pending operation info
        pending_write_pos_ = oieb->payload_write_pos;
        pending_frame_size_ = size;
        pending_frame_total_size_ = total_size;
        sequence_number = pending_frame_sequence_ = sequence_number_++;
        
        // Write frame header
        uint8_t* write_ptr = payload_start_ + oieb->payload_write_pos;
        FrameHeader header;
        header.payload_size = size;
        header.sequence_number = pending_frame_sequence_;
        std::memcpy(write_ptr, &header, sizeof(header));
        
        // Return pointer to frame data area
        return write_ptr + sizeof(header);
    }
    
    void commit_frame() {
        OIEB* oieb = get_oieb();
        
        // Update tracking
        oieb->payload_write_pos += pending_frame_total_size_;
        frames_written_++;
        bytes_written_ += pending_frame_size_;
        
        // Update OIEB
        oieb->payload_free_bytes -= pending_frame_total_size_;
        oieb->payload_written_count++;
        
        // Release memory barrier
        std::atomic_thread_fence(std::memory_order_release);
        
        // Signal reader
        sem_write_->signal();
    }
    
    bool is_reader_connected() const {
        const OIEB* oieb = get_oieb();
        return oieb->reader_pid != 0 && platform::process_exists(oieb->reader_pid);
    }
    
private:
    std::string name_;
    std::unique_ptr<SharedMemory> shm_;
    std::unique_ptr<Semaphore> sem_write_;
    std::unique_ptr<Semaphore> sem_read_;
    
    uint8_t* metadata_start_;
    uint8_t* payload_start_;
    uint64_t sequence_number_;
    uint64_t frames_written_;
    uint64_t bytes_written_;
    bool metadata_written_;
    
    // For zero-copy operations
    size_t pending_metadata_size_;
    size_t pending_frame_size_;
    uint64_t pending_frame_sequence_;
    size_t pending_frame_total_size_;
    size_t pending_write_pos_;
    
    friend class Writer;  // Allow Writer to access private members
};

Writer::Writer(const std::string& name)
    : impl_(std::make_unique<Impl>(name)), name_(name) {}

Writer::~Writer() = default;

Writer::Writer(Writer&&) noexcept = default;
Writer& Writer::operator=(Writer&&) noexcept = default;

void Writer::set_metadata(const void* data, size_t size) {
    impl_->set_metadata(data, size);
}

void Writer::set_metadata(const std::vector<uint8_t>& data) {
    impl_->set_metadata(data.data(), data.size());
}

void Writer::write_frame(const void* data, size_t size) {
    impl_->write_frame(data, size);
}

void Writer::write_frame(const std::vector<uint8_t>& data) {
    impl_->write_frame(data.data(), data.size());
}

void* Writer::get_metadata_buffer(size_t size) {
    return impl_->get_metadata_buffer(size);
}

void Writer::commit_metadata() {
    impl_->commit_metadata();
}

void* Writer::get_frame_buffer(size_t size, uint64_t& sequence_number) {
    return impl_->get_frame_buffer(size, sequence_number);
}

void Writer::commit_frame() {
    impl_->commit_frame();
}

bool Writer::is_reader_connected() const {
    return impl_->is_reader_connected();
}

uint64_t Writer::frames_written() const {
    return impl_->frames_written_;
}

uint64_t Writer::bytes_written() const {
    return impl_->bytes_written_;
}

} // namespace zerobuffer