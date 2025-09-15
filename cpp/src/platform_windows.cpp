#ifdef _WIN32

#include "zerobuffer/platform.h"
#include "zerobuffer/reader.h"  // For ZeroBufferException
#include <windows.h>
// Prevent Windows.h from defining min/max macros that conflict with std::min/std::max
#ifdef max
#undef max
#endif
#ifdef min
#undef min
#endif
#include <sstream>
#include <filesystem>
#include <algorithm>

namespace zerobuffer {
namespace platform {

uint64_t get_current_pid() {
    return static_cast<uint64_t>(GetCurrentProcessId());
}

bool process_exists(uint64_t pid) {
    if (pid == 0) return false;
    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION, FALSE, static_cast<DWORD>(pid));
    if (process == NULL) return false;
    
    DWORD exitCode;
    bool exists = GetExitCodeProcess(process, &exitCode) && exitCode == STILL_ACTIVE;
    CloseHandle(process);
    return exists;
}

std::string get_temp_directory() {
    char tempPath[MAX_PATH];
    GetTempPathA(MAX_PATH, tempPath);
    return std::string(tempPath) + "zerobuffer";
}

size_t align_to_boundary(size_t size, size_t alignment) {
    return (size + alignment - 1) & ~(alignment - 1);
}

} // namespace platform

// Windows SharedMemory implementation
class WindowsSharedMemory : public SharedMemory {
public:
    WindowsSharedMemory(const std::string& name, size_t size, bool create)
        : name_(name), size_(size), handle_(NULL), data_(nullptr) {
        
        std::string fullName = "Global\\" + name;
        
        if (create) {
            LARGE_INTEGER liSize;
            liSize.QuadPart = size;
            
            handle_ = CreateFileMappingA(
                INVALID_HANDLE_VALUE,
                NULL,
                PAGE_READWRITE,
                liSize.HighPart,
                liSize.LowPart,
                fullName.c_str()
            );
            
            if (handle_ == NULL) {
                throw ZeroBufferException("Failed to create shared memory: " + std::to_string(GetLastError()));
            }
            
            if (GetLastError() == ERROR_ALREADY_EXISTS) {
                CloseHandle(handle_);
                throw ZeroBufferException("Shared memory already exists");
            }
        } else {
            handle_ = OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, fullName.c_str());
            if (handle_ == NULL) {
                throw ZeroBufferException("Failed to open shared memory: " + std::to_string(GetLastError()));
            }
        }
        
        data_ = MapViewOfFile(handle_, FILE_MAP_ALL_ACCESS, 0, 0, size);
        if (data_ == nullptr) {
            CloseHandle(handle_);
            throw ZeroBufferException("Failed to map shared memory: " + std::to_string(GetLastError()));
        }
        
        if (create) {
            // Zero the memory
            memset(data_, 0, size_);
        }
    }
    
    ~WindowsSharedMemory() {
        if (data_ != nullptr) {
            UnmapViewOfFile(data_);
        }
        if (handle_ != NULL) {
            CloseHandle(handle_);
        }
    }
    
    void* data() override { return data_; }
    const void* data() const override { return data_; }
    size_t size() const override { return size_; }
    bool valid() const override { return data_ != nullptr; }
    
private:
    std::string name_;
    size_t size_;
    HANDLE handle_;
    void* data_;
};

std::unique_ptr<SharedMemory> SharedMemory::create(const std::string& name, size_t size) {
    return std::make_unique<WindowsSharedMemory>(name, size, true);
}

std::unique_ptr<SharedMemory> SharedMemory::open(const std::string& name) {
    return std::make_unique<WindowsSharedMemory>(name, 0, false);
}

void SharedMemory::remove(const std::string& name) {
    // Windows doesn't have persistent shared memory that needs cleanup
    // Shared memory is automatically cleaned up when all handles are closed
}

// Windows Semaphore implementation
class WindowsSemaphore : public Semaphore {
public:
    WindowsSemaphore(const std::string& name, unsigned int initial_value, bool create)
        : name_(name), handle_(NULL) {
        
        std::string fullName = "Global\\sem-" + name;
        
        if (create) {
            handle_ = CreateSemaphoreA(NULL, initial_value, LONG_MAX, fullName.c_str());
            if (handle_ == NULL) {
                throw ZeroBufferException("Failed to create semaphore: " + std::to_string(GetLastError()));
            }
            if (GetLastError() == ERROR_ALREADY_EXISTS) {
                CloseHandle(handle_);
                throw ZeroBufferException("Semaphore already exists");
            }
        } else {
            handle_ = OpenSemaphoreA(SYNCHRONIZE | SEMAPHORE_MODIFY_STATE, FALSE, fullName.c_str());
            if (handle_ == NULL) {
                throw ZeroBufferException("Failed to open semaphore: " + std::to_string(GetLastError()));
            }
        }
    }
    
    ~WindowsSemaphore() {
        if (handle_ != NULL) {
            CloseHandle(handle_);
        }
    }
    
    bool wait(std::chrono::milliseconds timeout) override {
        DWORD ms = (timeout == std::chrono::milliseconds::max()) ? INFINITE : static_cast<DWORD>(timeout.count());
        DWORD result = WaitForSingleObject(handle_, ms);
        return result == WAIT_OBJECT_0;
    }
    
    void signal() override {
        if (!ReleaseSemaphore(handle_, 1, NULL)) {
            throw ZeroBufferException("Failed to signal semaphore: " + std::to_string(GetLastError()));
        }
    }
    
private:
    std::string name_;
    HANDLE handle_;
};

std::unique_ptr<Semaphore> Semaphore::create(const std::string& name, unsigned int initial_value) {
    return std::make_unique<WindowsSemaphore>(name, initial_value, true);
}

std::unique_ptr<Semaphore> Semaphore::open(const std::string& name) {
    return std::make_unique<WindowsSemaphore>(name, 0, false);
}

void Semaphore::remove(const std::string& name) {
    // Windows doesn't have persistent named semaphores that need cleanup
}

// Windows FileLock implementation
class WindowsFileLock : public FileLock {
public:
    explicit WindowsFileLock(const std::string& path) : path_(path), handle_(INVALID_HANDLE_VALUE) {
        // Create directory if it doesn't exist
        std::filesystem::create_directories(std::filesystem::path(path).parent_path());
        
        handle_ = CreateFileA(
            path.c_str(),
            GENERIC_READ | GENERIC_WRITE,
            0, // No sharing
            NULL,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_DELETE_ON_CLOSE,
            NULL
        );
        
        if (handle_ == INVALID_HANDLE_VALUE) {
            throw ZeroBufferException("Failed to create lock file: " + std::to_string(GetLastError()));
        }
    }
    
    ~WindowsFileLock() {
        if (handle_ != INVALID_HANDLE_VALUE) {
            CloseHandle(handle_);
        }
    }
    
    bool is_locked() const override {
        return handle_ != INVALID_HANDLE_VALUE;
    }
    
private:
    std::string path_;
    HANDLE handle_;
};

std::unique_ptr<FileLock> FileLock::create(const std::string& path) {
    return std::make_unique<WindowsFileLock>(path);
}

bool FileLock::try_remove_stale(const std::string& path) {
    // Try to open exclusively
    HANDLE handle = CreateFileA(
        path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0, // No sharing
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL
    );
    
    if (handle == INVALID_HANDLE_VALUE) {
        // Either doesn't exist or is locked
        return GetLastError() == ERROR_FILE_NOT_FOUND;
    }
    
    // We got exclusive access, file is stale
    CloseHandle(handle);
    return DeleteFileA(path.c_str()) != 0;
}

} // namespace zerobuffer

#endif // _WIN32