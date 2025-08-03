using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace ZeroBuffer
{
    /// <summary>
    /// Windows implementation of file locking
    /// </summary>
    internal sealed class WindowsFileLock : IFileLock
    {
        private readonly string _path;
        private readonly FileStream _fileStream;
        private bool _disposed;
        
        public WindowsFileLock(string path)
        {
            _path = path;
            
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            try
            {
                // Open with exclusive access - this provides the lock
                _fileStream = new FileStream(path, 
                    FileMode.OpenOrCreate, 
                    FileAccess.ReadWrite, 
                    FileShare.None, // Exclusive access
                    4096, 
                    FileOptions.DeleteOnClose); // Auto-delete when closed
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Failed to acquire lock on '{path}': {ex.Message}", ex);
            }
        }
        
        public bool IsLocked => _fileStream != null;
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            _fileStream?.Dispose();
            
            // Try to delete the file if it still exists (DeleteOnClose might fail)
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
        
        /// <summary>
        /// Try to remove a stale lock file
        /// </summary>
        public static bool TryRemoveStale(string path)
        {
            try
            {
                // Try to open with exclusive access
                using (var fs = new FileStream(path, 
                    FileMode.Open, 
                    FileAccess.ReadWrite, 
                    FileShare.None))
                {
                    // We got exclusive access, so it's stale
                }
                
                // Delete the stale file
                File.Delete(path);
                return true;
            }
            catch
            {
                // Either file doesn't exist or is locked by another process
                return false;
            }
        }
    }
}