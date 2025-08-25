#ifdef __linux__

#include "zerobuffer/platform.h"
#include "zerobuffer/reader.h"  // For ZeroBufferException
#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#include <semaphore.h>
#include <signal.h>
#include <sys/file.h>
#include <cstring>
#include <sstream>
#include <filesystem>
#include <fstream>

namespace zerobuffer {
namespace platform {

uint64_t get_current_pid() {
    return static_cast<uint64_t>(getpid());
}

bool process_exists(uint64_t pid) {
    if (pid == 0) return false;
    return kill(static_cast<pid_t>(pid), 0) == 0;
}

uint64_t get_process_start_time(uint64_t pid) {
    if (pid == 0) return 0;
    
    // Read /proc/[pid]/stat to get process start time
    std::string stat_path = "/proc/" + std::to_string(pid) + "/stat";
    std::ifstream stat_file(stat_path);
    if (!stat_file) {
        return 0; // Process doesn't exist
    }
    
    std::string line;
    std::getline(stat_file, line);
    
    // Parse the stat file - start time is the 22nd field
    // Skip to the end of the command field (enclosed in parentheses)
    size_t cmd_end = line.rfind(')');
    if (cmd_end == std::string::npos) {
        return 0;
    }
    
    // Parse fields after the command
    std::istringstream iss(line.substr(cmd_end + 1));
    uint64_t field;
    
    // Skip fields 3-21 (we need field 22)
    for (int i = 3; i <= 21; ++i) {
        if (!(iss >> field)) {
            return 0;
        }
    }
    
    // Field 22 is start time in clock ticks since boot
    uint64_t start_time;
    if (!(iss >> start_time)) {
        return 0;
    }
    
    return start_time;
}

uint64_t get_current_process_start_time() {
    return get_process_start_time(static_cast<uint64_t>(getpid()));
}

std::string get_temp_directory() {
    return "/tmp/zerobuffer";
}

size_t align_to_boundary(size_t size, size_t alignment) {
    return (size + alignment - 1) & ~(alignment - 1);
}

} // namespace platform

// Linux SharedMemory implementation
class LinuxSharedMemory : public SharedMemory {
public:
    LinuxSharedMemory(const std::string& name, size_t size, bool create)
        : name_(name), size_(size), fd_(-1), data_(nullptr) {
        
        int flags = create ? (O_CREAT | O_EXCL | O_RDWR) : O_RDWR;
        mode_t mode = S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP | S_IROTH | S_IWOTH;  // 0666 - read/write for all
        
        fd_ = shm_open(name.c_str(), flags, mode);
        if (fd_ == -1) {
            throw ZeroBufferException("Failed to open shared memory: " + std::string(strerror(errno)));
        }
        
        if (create) {
            if (ftruncate(fd_, static_cast<off_t>(size)) == -1) {
                close(fd_);
                shm_unlink(name.c_str());
                throw ZeroBufferException("Failed to resize shared memory: " + std::string(strerror(errno)));
            }
        } else {
            // Get actual size
            struct stat st;
            if (fstat(fd_, &st) == -1) {
                close(fd_);
                throw ZeroBufferException("Failed to stat shared memory: " + std::string(strerror(errno)));
            }
            size_ = static_cast<size_t>(st.st_size);
        }
        
        data_ = mmap(nullptr, size_, PROT_READ | PROT_WRITE, MAP_SHARED, fd_, 0);
        if (data_ == MAP_FAILED) {
            close(fd_);
            if (create) shm_unlink(name.c_str());
            throw ZeroBufferException("Failed to map shared memory: " + std::string(strerror(errno)));
        }
        
        if (create) {
            // Zero the memory
            memset(data_, 0, size_);
        }
    }
    
    ~LinuxSharedMemory() {
        if (data_ != nullptr && data_ != MAP_FAILED) {
            munmap(data_, size_);
        }
        if (fd_ != -1) {
            close(fd_);
        }
    }
    
    void* data() override { return data_; }
    const void* data() const override { return data_; }
    size_t size() const override { return size_; }
    bool valid() const override { return data_ != nullptr && data_ != MAP_FAILED; }
    
    void unlink() {
        shm_unlink(name_.c_str());
    }
    
private:
    std::string name_;
    size_t size_;
    int fd_;
    void* data_;
};

std::unique_ptr<SharedMemory> SharedMemory::create(const std::string& name, size_t size) {
    auto ptr = std::make_unique<LinuxSharedMemory>(name, size, true);
    return ptr;
}

std::unique_ptr<SharedMemory> SharedMemory::open(const std::string& name) {
    return std::make_unique<LinuxSharedMemory>(name, 0, false);
}

void SharedMemory::remove(const std::string& name) {
    shm_unlink(name.c_str());
    // Ignore errors - shared memory might not exist
}

// Linux Semaphore implementation
class LinuxSemaphore : public Semaphore {
public:
    LinuxSemaphore(const std::string& name, unsigned int initial_value, bool create)
        : name_(name), sem_(nullptr) {
        
        if (create) {
            sem_ = sem_open(name.c_str(), O_CREAT | O_EXCL, S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP | S_IROTH | S_IWOTH, initial_value);  // 0666
        } else {
            sem_ = sem_open(name.c_str(), 0);
        }
        
        if (sem_ == SEM_FAILED) {
            throw ZeroBufferException("Failed to open semaphore: " + std::string(strerror(errno)));
        }
    }
    
    ~LinuxSemaphore() {
        if (sem_ != nullptr && sem_ != SEM_FAILED) {
            sem_close(sem_);
        }
    }
    
    bool wait(std::chrono::milliseconds timeout) override {
        if (timeout == std::chrono::milliseconds::max()) {
            return sem_wait(sem_) == 0;
        }
        
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        
        auto nsec = std::chrono::duration_cast<std::chrono::nanoseconds>(timeout).count();
        ts.tv_sec += nsec / 1000000000;
        ts.tv_nsec += nsec % 1000000000;
        
        if (ts.tv_nsec >= 1000000000) {
            ts.tv_sec += 1;
            ts.tv_nsec -= 1000000000;
        }
        
        int ret = sem_timedwait(sem_, &ts);
        return ret == 0;
    }
    
    void signal() override {
        if (sem_post(sem_) != 0) {
            throw ZeroBufferException("Failed to signal semaphore: " + std::string(strerror(errno)));
        }
    }
    
private:
    std::string name_;
    sem_t* sem_;
};

std::unique_ptr<Semaphore> Semaphore::create(const std::string& name, unsigned int initial_value) {
    return std::make_unique<LinuxSemaphore>(name, initial_value, true);
}

std::unique_ptr<Semaphore> Semaphore::open(const std::string& name) {
    return std::make_unique<LinuxSemaphore>(name, 0, false);
}

void Semaphore::remove(const std::string& name) {
    sem_unlink(name.c_str());
    // Ignore errors - semaphore might not exist
}

// Linux FileLock implementation
class LinuxFileLock : public FileLock {
public:
    explicit LinuxFileLock(const std::string& path) : path_(path), fd_(-1) {
        // Create directory if it doesn't exist
        std::filesystem::create_directories(std::filesystem::path(path).parent_path());
        
        fd_ = open(path.c_str(), O_CREAT | O_RDWR, S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP | S_IROTH | S_IWOTH);  // 0666
        if (fd_ == -1) {
            throw ZeroBufferException("Failed to create lock file: " + std::string(strerror(errno)));
        }
        
        // Use flock for inter-process locking
        if (flock(fd_, LOCK_EX | LOCK_NB) == -1) {
            close(fd_);
            fd_ = -1;
            throw ZeroBufferException("Failed to acquire lock: " + std::string(strerror(errno)));
        }
    }
    
    ~LinuxFileLock() {
        if (fd_ != -1) {
            close(fd_);
            unlink(path_.c_str());
        }
    }
    
    bool is_locked() const override {
        return fd_ != -1;
    }
    
private:
    std::string path_;
    int fd_;
};

std::unique_ptr<FileLock> FileLock::create(const std::string& path) {
    return std::make_unique<LinuxFileLock>(path);
}

bool FileLock::try_remove_stale(const std::string& path) {
    // Try to open with exclusive lock
    int fd = open(path.c_str(), O_RDWR);
    if (fd == -1) {
        // File doesn't exist, nothing to clean
        return false;
    }
    
    // Try to acquire exclusive lock with flock
    if (flock(fd, LOCK_EX | LOCK_NB) == 0) {
        // We got the lock, file is stale
        flock(fd, LOCK_UN);
        close(fd);
        return unlink(path.c_str()) == 0;
    }
    
    close(fd);
    return false;
}

} // namespace zerobuffer

#endif // __linux__