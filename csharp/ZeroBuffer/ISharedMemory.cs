using System;

namespace ZeroBuffer
{
    /// <summary>
    /// Abstraction for cross-platform shared memory operations
    /// </summary>
    internal interface ISharedMemory : IDisposable
    {
        /// <summary>
        /// Gets the name of the shared memory segment
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Gets the size of the shared memory segment
        /// </summary>
        long Size { get; }
        
        /// <summary>
        /// Reads a structure from the specified offset
        /// </summary>
        T Read<T>(long offset) where T : struct;
        
        /// <summary>
        /// Gets a readonly reference to a structure at the specified offset (zero-copy)
        /// </summary>
        ref readonly T ReadRef<T>(long offset) where T : struct;
        
        /// <summary>
        /// Writes a structure to the specified offset
        /// </summary>
        void Write<T>(long offset, in T value) where T : struct;
        
        /// <summary>
        /// Reads an array from the specified offset
        /// </summary>
        void ReadArray(long offset, byte[] buffer, int index, int count);
        
        /// <summary>
        /// Writes an array to the specified offset
        /// </summary>
        void WriteArray(long offset, byte[] buffer, int index, int count);
        
        /// <summary>
        /// Writes a span to the specified offset (zero allocation)
        /// </summary>
        void WriteSpan(long offset, ReadOnlySpan<byte> data);
        
        /// <summary>
        /// Ensures memory changes are visible to other processes
        /// </summary>
        void Flush();
        
        /// <summary>
        /// Get unsafe pointer to the shared memory at the specified offset
        /// For zero-copy access
        /// </summary>
        unsafe byte* GetPointer(long offset);
    }
    
    /// <summary>
    /// Factory for creating platform-specific shared memory implementations
    /// </summary>
    internal static class SharedMemoryFactory
    {
        public static ISharedMemory Create(string name, long size)
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsSharedMemory(name, size);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return new PosixSharedMemory(name, size);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform");
            }
        }
        
        public static ISharedMemory Open(string name)
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsSharedMemory.OpenExisting(name);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return PosixSharedMemory.OpenExisting(name);
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
                // But we can try to open and immediately close to help with cleanup
                try
                {
                    using var temp = WindowsSharedMemory.OpenExisting(name);
                }
                catch { }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                PosixSharedMemory.Remove(name);
            }
        }
    }
}