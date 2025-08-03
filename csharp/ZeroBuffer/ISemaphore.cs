using System;

namespace ZeroBuffer
{
    /// <summary>
    /// Abstraction for cross-platform named semaphore operations
    /// </summary>
    internal interface ISemaphore : IDisposable
    {
        /// <summary>
        /// Waits on the semaphore with optional timeout
        /// </summary>
        /// <returns>True if acquired, false if timed out</returns>
        bool Wait(TimeSpan? timeout = null);
        
        /// <summary>
        /// Releases the semaphore
        /// </summary>
        void Release();
        
        /// <summary>
        /// Tries to acquire the semaphore without blocking
        /// </summary>
        bool TryWait();
    }
    
    /// <summary>
    /// Factory for creating platform-specific semaphore implementations
    /// </summary>
    internal static class SemaphoreFactory
    {
        public static ISemaphore Create(string name, int initialCount = 0)
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsSemaphore(name, initialCount);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return new PosixSemaphore(name, initialCount);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform");
            }
        }
        
        public static ISemaphore Open(string name)
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsSemaphore.OpenExisting(name);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return PosixSemaphore.OpenExisting(name);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform");
            }
        }
        
        public static void Remove(string name)
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows doesn't have explicit removal - resources are cleaned up when last handle closes
                try
                {
                    using var temp = WindowsSemaphore.OpenExisting(name);
                }
                catch { }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                PosixSemaphore.Remove(name);
            }
        }
    }
}