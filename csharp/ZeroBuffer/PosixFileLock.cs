using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// POSIX implementation of file locking using flock
    /// </summary>
    internal sealed class PosixFileLock : IFileLock
    {
        private readonly string _path;
        private readonly int _fd;
        private bool _disposed;
        
        // flock constants
        private const int LOCK_EX = 2;  // Exclusive lock
        private const int LOCK_NB = 4;  // Non-blocking
        private const int LOCK_UN = 8;  // Unlock
        
        public PosixFileLock(string path)
        {
            // Ensure P/Invoke resolver is initialized before any native calls
            PosixInterop.EnsureInitialized();

            _path = path;

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Create or open the lock file
            _fd = PosixInterop.open(path, PosixInterop.O_CREAT | PosixInterop.O_RDWR, 
                                         PosixInterop.S_IRUSR | PosixInterop.S_IWUSR |
                                         PosixInterop.S_IRGRP | PosixInterop.S_IWGRP |
                                         PosixInterop.S_IROTH | PosixInterop.S_IWOTH);  // 0666
            
            if (_fd == -1)
            {
                throw new InvalidOperationException($"Failed to create lock file '{path}': {PosixInterop.GetLastError()}");
            }
            
            // Try to acquire exclusive lock (non-blocking)
            if (PosixInterop.flock(_fd, LOCK_EX | LOCK_NB) == -1)
            {
                PosixInterop.close(_fd);
                throw new InvalidOperationException($"Failed to acquire lock on '{path}': {PosixInterop.GetLastError()}");
            }
        }
        
        public bool IsLocked => _fd != -1;
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            if (_fd != -1)
            {
                // Release lock and close file
                PosixInterop.flock(_fd, LOCK_UN);
                PosixInterop.close(_fd);
                
                // Remove lock file
                try
                {
                    PosixInterop.unlink(_path);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
        
        /// <summary>
        /// Try to remove a stale lock file
        /// </summary>
        public static bool TryRemoveStale(string path)
        {
            // Ensure P/Invoke resolver is initialized before any native calls
            PosixInterop.EnsureInitialized();

            // Try to open the lock file
            int fd = PosixInterop.open(path, PosixInterop.O_RDWR, 0);
            if (fd == -1)
            {
                // File doesn't exist, nothing to clean
                return false;
            }
            
            // Try to acquire exclusive lock (non-blocking)
            if (PosixInterop.flock(fd, LOCK_EX | LOCK_NB) == 0)
            {
                // We got the lock, file is stale
                PosixInterop.flock(fd, LOCK_UN);
                PosixInterop.close(fd);
                
                // Remove the stale lock file
                return PosixInterop.unlink(path) == 0;
            }
            
            // Lock is held by another process
            PosixInterop.close(fd);
            return false;
        }
    }
}