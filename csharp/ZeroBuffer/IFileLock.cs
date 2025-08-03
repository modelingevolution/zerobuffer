using System;

namespace ZeroBuffer
{
    /// <summary>
    /// Abstraction for cross-platform file locking to track resource ownership
    /// </summary>
    internal interface IFileLock : IDisposable
    {
        /// <summary>
        /// Check if lock is successfully held
        /// </summary>
        bool IsLocked { get; }
    }
    
    /// <summary>
    /// Factory for creating platform-specific file lock implementations
    /// </summary>
    internal static class FileLockFactory
    {
        public static IFileLock Create(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsFileLock(path);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return new PosixFileLock(path);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform");
            }
        }
        
        public static bool TryRemoveStale(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsFileLock.TryRemoveStale(path);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return PosixFileLock.TryRemoveStale(path);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform");
            }
        }
    }
}