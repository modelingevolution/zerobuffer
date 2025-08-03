#ifndef ZEROBUFFER_PLATFORM_H
#define ZEROBUFFER_PLATFORM_H

#include <string>
#include <memory>
#include <chrono>

namespace zerobuffer {

// Forward declarations
class SharedMemory;
class Semaphore;
class FileLock;

// Platform-specific implementations
namespace platform {

// Get current process ID
uint64_t get_current_pid();

// Check if process exists
bool process_exists(uint64_t pid);

// Get process start time (to handle PID reuse)
uint64_t get_process_start_time(uint64_t pid);

// Get current process start time
uint64_t get_current_process_start_time();

// Get temp directory for lock files
std::string get_temp_directory();

// Align size to block boundary
size_t align_to_boundary(size_t size, size_t alignment);

} // namespace platform

// Shared memory abstraction
class SharedMemory {
public:
    virtual ~SharedMemory() = default;
    
    // Create new shared memory
    static std::unique_ptr<SharedMemory> create(const std::string& name, size_t size);
    
    // Open existing shared memory
    static std::unique_ptr<SharedMemory> open(const std::string& name);
    
    // Get memory pointer
    virtual void* data() = 0;
    virtual const void* data() const = 0;
    
    // Get size
    virtual size_t size() const = 0;
    
    // Check if valid
    virtual bool valid() const = 0;
    
    // Remove shared memory (static cleanup)
    static void remove(const std::string& name);
};

// Semaphore abstraction
class Semaphore {
public:
    virtual ~Semaphore() = default;
    
    // Create or open semaphore
    static std::unique_ptr<Semaphore> create(const std::string& name, unsigned int initial_value);
    
    // Open existing semaphore
    static std::unique_ptr<Semaphore> open(const std::string& name);
    
    // Wait on semaphore (with timeout)
    virtual bool wait(std::chrono::milliseconds timeout = std::chrono::milliseconds::max()) = 0;
    
    // Signal semaphore
    virtual void signal() = 0;
    
    // Try to remove semaphore (static cleanup)
    static void remove(const std::string& name);
};

// File lock abstraction
class FileLock {
public:
    virtual ~FileLock() = default;
    
    // Create file lock
    static std::unique_ptr<FileLock> create(const std::string& path);
    
    // Check if lock is held
    virtual bool is_locked() const = 0;
    
    // Try to remove stale lock file
    static bool try_remove_stale(const std::string& path);
};

} // namespace zerobuffer

#endif // ZEROBUFFER_PLATFORM_H