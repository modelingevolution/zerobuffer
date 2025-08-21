#include "zerobuffer/reader.h"
#include "zerobuffer/logger.h"
#include <cstring>
#include <atomic>
#include <filesystem>
#include <thread>
#include <chrono>
#include <boost/log/attributes/named_scope.hpp>

namespace zerobuffer {

class Reader::Impl {
public:
    Impl(const std::string& name, const BufferConfig& config) 
        : name_(name), config_(config), expected_sequence_(1), frames_read_(0), bytes_read_(0) {
        
        ZEROBUFFER_LOG_DEBUG("Reader") << "Creating buffer: " << name;
        
        // Create lock file
        std::string lock_path = platform::get_temp_directory() + "/" + name + ".lock";
        
        // Try to clean up stale resources
        cleanup_stale_resources();
        
        // Create lock
        lock_ = FileLock::create(lock_path);
        
        // Calculate aligned sizes
        size_t oieb_size = sizeof(OIEB);
        size_t metadata_size = platform::align_to_boundary(config.metadata_size, BLOCK_ALIGNMENT);
        size_t payload_size = platform::align_to_boundary(config.payload_size, BLOCK_ALIGNMENT);
        size_t total_size = oieb_size + metadata_size + payload_size;
        
        // Create shared memory
        try {
            shm_ = SharedMemory::create(name, total_size);
        } catch (const std::exception&) {
            // Try to clean up existing resources
            try {
                SharedMemory::remove(name);
                Semaphore::remove("/sem-w-" + name);
                Semaphore::remove("/sem-r-" + name);
            } catch (...) {}
            // Retry
            shm_ = SharedMemory::create(name, total_size);
        }
        
        // Initialize OIEB
        OIEB* oieb = get_oieb();
        oieb->oieb_size = 128;  // Always 128 for v1.x.x
        oieb->version = ProtocolVersion(1, 0, 0);  // Version 1.0.0
        oieb->metadata_size = metadata_size;
        oieb->metadata_free_bytes = metadata_size;
        oieb->metadata_written_bytes = 0;
        oieb->payload_size = payload_size;
        oieb->payload_free_bytes = payload_size;
        oieb->payload_write_pos = 0;
        oieb->payload_read_pos = 0;
        oieb->payload_written_count = 0;
        oieb->payload_read_count = 0;
        oieb->writer_pid = 0;
        oieb->reader_pid = platform::get_current_pid();
        
        // Create semaphores
        sem_write_ = Semaphore::create("/sem-w-" + name, 0);
        sem_read_ = Semaphore::create("/sem-r-" + name, 0);
        
        // Set up pointers
        metadata_start_ = reinterpret_cast<uint8_t*>(shm_->data()) + oieb_size;
        payload_start_ = metadata_start_ + metadata_size;
    }
    
    ~Impl() {
        // Clear reader PID
        if (shm_ && shm_->valid()) {
            OIEB* oieb = get_oieb();
            oieb->reader_pid = 0;
        }
        
        // Reader owns the resources and must clean them up on destruction
        // First close our handles
        sem_write_.reset();
        sem_read_.reset();
        shm_.reset();
        lock_.reset();
        
        // Then remove the system resources
        try {
            SharedMemory::remove(name_);
            Semaphore::remove("/sem-w-" + name_);
            Semaphore::remove("/sem-r-" + name_);
        } catch (...) {
            // Ignore errors during cleanup
        }
    }
    
    OIEB* get_oieb() {
        return reinterpret_cast<OIEB*>(shm_->data());
    }
    
    const OIEB* get_oieb() const {
        return reinterpret_cast<const OIEB*>(shm_->data());
    }
    
    void cleanup_stale_resources() {
        std::string lock_dir = platform::get_temp_directory();
        try {
            // Create the directory if it doesn't exist
            std::filesystem::create_directories(lock_dir);
            
            for (const auto& entry : std::filesystem::directory_iterator(lock_dir)) {
                if (entry.path().extension() == ".lock") {
                    std::string lock_path = entry.path().string();
                    if (FileLock::try_remove_stale(lock_path)) {
                        // We successfully removed a stale lock, clean up associated resources
                        std::string filename = entry.path().stem().string();
                        
                        try {
                            // Check if shared memory exists and is orphaned
                            auto shm = SharedMemory::open(filename);
                            OIEB* oieb = reinterpret_cast<OIEB*>(shm->data());
                            
                            // Check if both reader and writer are dead
                            bool reader_dead = (oieb->reader_pid == 0) || !platform::process_exists(oieb->reader_pid);
                            bool writer_dead = (oieb->writer_pid == 0) || !platform::process_exists(oieb->writer_pid);
                            
                            if (reader_dead && writer_dead) {
                                // Both processes are dead, safe to clean up
                                shm.reset();
                                Semaphore::remove("/sem-w-" + filename);
                                Semaphore::remove("/sem-r-" + filename);
                                SharedMemory::remove(filename);
                            }
                        } catch (...) {
                            // If we can't open shared memory, clean up anyway since lock was stale
                            try {
                                Semaphore::remove("/sem-w-" + filename);
                                Semaphore::remove("/sem-r-" + filename);
                                SharedMemory::remove(filename);
                            } catch (...) {
                                // Ignore errors
                            }
                        }
                    }
                }
            }
        } catch (...) {
            // Ignore errors during cleanup
        }
    }
    
    std::vector<uint8_t> get_metadata() const {
        const OIEB* oieb = get_oieb();
        if (oieb->metadata_written_bytes == 0) {
            return std::vector<uint8_t>();
        }
        
        // Read metadata size
        uint64_t meta_size;
        std::memcpy(&meta_size, metadata_start_, sizeof(meta_size));
        
        if (meta_size == 0 || meta_size > oieb->metadata_written_bytes - sizeof(uint64_t)) {
            throw ZeroBufferException("Invalid metadata size");
        }
        
        std::vector<uint8_t> metadata(meta_size);
        std::memcpy(metadata.data(), metadata_start_ + sizeof(uint64_t), meta_size);
        return metadata;
    }
    
    const void* get_metadata_raw() const {
        const OIEB* oieb = get_oieb();
        if (oieb->metadata_written_bytes == 0) {
            return nullptr;
        }
        
        // Return pointer to metadata (skip size prefix)
        return metadata_start_ + sizeof(uint64_t);
    }
    
    size_t get_metadata_size() const {
        const OIEB* oieb = get_oieb();
        if (oieb->metadata_written_bytes == 0) {
            return 0;
        }
        
        uint64_t meta_size;
        std::memcpy(&meta_size, metadata_start_, sizeof(meta_size));
        return static_cast<size_t>(meta_size);
    }
    
    Frame read_frame_with_timeout(std::chrono::milliseconds timeout) {
        auto start = std::chrono::steady_clock::now();
        
        while (true) {
            // Calculate remaining timeout
            auto elapsed = std::chrono::steady_clock::now() - start;
            if (elapsed >= timeout) {
                // Return invalid frame on timeout
                return Frame();
            }
            
            auto remaining = timeout - std::chrono::duration_cast<std::chrono::milliseconds>(elapsed);
            
            // Wait for data with remaining timeout
            if (!sem_write_->wait(remaining)) {
                // Timeout - check if writer is alive
                const OIEB* oieb = get_oieb();
                if (oieb->writer_pid != 0 && !platform::process_exists(oieb->writer_pid)) {
                    throw WriterDeadException();
                }
                // Return invalid frame on timeout
                return Frame();
            }
            
            // Acquire memory barrier
            std::atomic_thread_fence(std::memory_order_acquire);
            
            OIEB* oieb = get_oieb();
            
            // Quick check to ensure writer hasn't disconnected gracefully
            // When writer_pid == 0 we can check payload_written_count as it won't be changed anymore by external process
            if (oieb->writer_pid == 0 && oieb->payload_written_count <= oieb->payload_read_count) {
                throw WriterDeadException();
            }
            
            // Read frame header directly via pointer - no copy needed
            const uint8_t* read_ptr = payload_start_ + oieb->payload_read_pos;
            const FrameHeader* header = reinterpret_cast<const FrameHeader*>(read_ptr);
            
            // Check for wrap-around marker (payload_size == 0)
            if (header->payload_size == 0) {
                ZEROBUFFER_LOG_DEBUG("Reader") << "Found wrap marker at position " << oieb->payload_read_pos
                    << ", handling wrap-around";
                
                // Calculate wasted space from current read position to end of buffer
                uint64_t wasted_space = oieb->payload_size - oieb->payload_read_pos;
                
                ZEROBUFFER_LOG_DEBUG("Reader") << "Wrap-around: wasted space = " << wasted_space 
                    << " bytes (from " << oieb->payload_read_pos << " to " << oieb->payload_size << ")";
                
                // Add back the wasted space to free bytes
                oieb->payload_free_bytes += wasted_space;
                
                // This is a wrap marker, move to beginning of buffer
                oieb->payload_read_pos = 0;
                oieb->payload_read_count++;  // Count the wrap marker as a "frame"
                
                ZEROBUFFER_LOG_DEBUG("Reader") << "After wrap: readPos=0, freeBytes=" << oieb->payload_free_bytes;
                
                // Don't signal semaphore for wrap marker - it's not a logical frame
                // Continue immediately to read the actual frame at the beginning
                // without going back through the semaphore wait
                read_ptr = payload_start_ + oieb->payload_read_pos;
                header = reinterpret_cast<const FrameHeader*>(read_ptr);
                
                // Now header points to the actual frame at the beginning
            }
            
            // Validate sequence number
            if (header->sequence_number != expected_sequence_) {
                throw SequenceError(expected_sequence_, header->sequence_number);
            }
            
            // Validate frame size
            if (header->payload_size == 0) {
                throw ZeroBufferException("Invalid frame size: 0");
            }
            
            size_t total_frame_size = sizeof(FrameHeader) + header->payload_size;
            
            // Check if frame wraps around buffer
            if (oieb->payload_read_pos + total_frame_size > oieb->payload_size) {
                // Frame would extend beyond buffer - check if we need to wrap to beginning
                if (oieb->payload_write_pos < oieb->payload_read_pos) {
                    // Writer has wrapped, we should wrap too
                    // This shouldn't happen if writer correctly writes wrap markers,
                    // but handle it just in case
                    
                    // Calculate wasted space from current read position to end of buffer
                    uint64_t wasted_space = oieb->payload_size - oieb->payload_read_pos;
                    
                    // Add back the wasted space to free bytes
                    oieb->payload_free_bytes += wasted_space;
                    
                    oieb->payload_read_pos = 0;
                    // Re-read header at new position
                    read_ptr = payload_start_ + oieb->payload_read_pos;
                    header = reinterpret_cast<const FrameHeader*>(read_ptr);
                    
                    // Re-validate sequence number after wrap
                    if (header->sequence_number != expected_sequence_) {
                        throw SequenceError(expected_sequence_, header->sequence_number);
                    }
                } else {
                    // Writer hasn't wrapped yet, wait
                    continue;
                }
            }
            
            // Create frame reference with RAII - zero allocations
            const void* frame_data = read_ptr + sizeof(FrameHeader);
            
            // Create release info with raw pointer to this impl and static callback
            Frame::ReleaseInfo release_info{
                this, 
                total_frame_size,
                [](void* impl, uint64_t size) {
                    static_cast<Reader::Impl*>(impl)->signal_frame_consumed(size);
                }
            };
            
            // Create frame with release info
            Frame frame(frame_data, header->payload_size, header->sequence_number, release_info);
            
            // Update OIEB immediately for read position tracking
            oieb->payload_read_pos += total_frame_size;
            if (oieb->payload_read_pos >= oieb->payload_size) {
                oieb->payload_read_pos -= oieb->payload_size;
            }
            oieb->payload_read_count++;
            
            // Note: We do NOT update payload_free_bytes or signal semaphore here!
            // This will be done when the Frame is destroyed (RAII)
            
            // Update tracking
            current_frame_size_ = total_frame_size;
            expected_sequence_++;
            frames_read_++;
            bytes_read_ += header->payload_size;
            
            return frame;
        }
    }
    
    void signal_frame_consumed(uint64_t frame_size) {
        // Called by Frame destructor via custom deleter
        ZEROBUFFER_LOG_DEBUG("Reader") << "Frame consumed, releasing " << frame_size << " bytes";
        
        // Update OIEB - this is what was missing!
        OIEB* oieb = get_oieb();
        
        // Add back the frame size to free bytes
        oieb->payload_free_bytes += frame_size;
        
        // Release memory barrier to ensure OIEB updates are visible
        std::atomic_thread_fence(std::memory_order_release);
        
        // Signal writer that space is available
        sem_read_->signal();
    }
    
    void release_frame(const Frame& frame) {
        // No-op: Frame destructor handles release via RAII
        // This method is kept for backward compatibility
        (void)frame;
    }
    
    bool is_writer_connected() const {
        const OIEB* oieb = get_oieb();
        return oieb->writer_pid != 0 && platform::process_exists(oieb->writer_pid);
    }
    
    bool is_writer_connected(int timeout_ms) const {
        using namespace std::chrono;
        auto start = high_resolution_clock::now();
        auto timeout = milliseconds(timeout_ms);
        
        while (true) {
            if (is_writer_connected()) {
                return true;
            }
            
            auto elapsed = high_resolution_clock::now() - start;
            if (elapsed >= timeout) {
                return false;
            }
            
            // Sleep for a short time before checking again
            std::this_thread::sleep_for(milliseconds(100));
        }
    }
    
private:
    std::string name_;
    BufferConfig config_;
    std::unique_ptr<FileLock> lock_;
    std::unique_ptr<SharedMemory> shm_;
    std::unique_ptr<Semaphore> sem_write_;
    std::unique_ptr<Semaphore> sem_read_;
    
    uint8_t* metadata_start_;
    uint8_t* payload_start_;
    size_t current_frame_size_;
    uint64_t expected_sequence_;
    uint64_t frames_read_;
    uint64_t bytes_read_;
    
    friend class Reader;  // Allow Reader to access private members
    friend struct Frame::ReleaseInfo;  // Allow ReleaseInfo to call signal_frame_consumed
};

Reader::Reader(const std::string& name, const BufferConfig& config)
    : impl_(std::make_unique<Impl>(name, config)), name_(name) {}

Reader::~Reader() = default;

Reader::Reader(Reader&&) noexcept = default;
Reader& Reader::operator=(Reader&&) noexcept = default;

std::vector<uint8_t> Reader::get_metadata() const {
    return impl_->get_metadata();
}

Frame Reader::read_frame(std::chrono::milliseconds timeout) {
    return impl_->read_frame_with_timeout(timeout);
}

void Reader::release_frame(const Frame& frame) {
    impl_->release_frame(frame);
}

bool Reader::is_writer_connected() const {
    return impl_->is_writer_connected();
}

bool Reader::is_writer_connected(int timeout_ms) const {
    return impl_->is_writer_connected(timeout_ms);
}

uint64_t Reader::frames_read() const {
    return impl_->frames_read_;
}

uint64_t Reader::bytes_read() const {
    return impl_->bytes_read_;
}

size_t Reader::get_metadata_size() const {
    return impl_->get_metadata_size();
}

const void* Reader::get_metadata_raw() const {
    return impl_->get_metadata_raw();
}

} // namespace zerobuffer