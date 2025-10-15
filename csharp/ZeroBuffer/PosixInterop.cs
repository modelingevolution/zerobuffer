using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// P/Invoke declarations for POSIX shared memory and semaphore functions
    /// </summary>
    internal static class PosixInterop
    {
        private static bool _initialized = false;
        private static readonly object _initLock = new object();

        // Static constructor to set up DLL import resolver
        // Called automatically by CLR before any member access - guaranteed thread-safe
        static PosixInterop()
        {
            EnsureInitialized();
        }

        // Explicit initialization method that can be called to ensure resolver is set up
        // Uses double-check locking pattern for thread-safety and performance
        internal static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_initLock)
            {
                if (_initialized)
                    return;

                NativeLibrary.SetDllImportResolver(typeof(PosixInterop).Assembly, DllImportResolver);
                _initialized = true;
            }
        }

        // Custom DLL import resolver that tries multiple library variants
        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // Only handle "libc" - let other libraries use default resolution
            if (libraryName != "libc")
                return IntPtr.Zero;

            // Try multiple library names in order of preference
            // On most Linux systems, shm_open/sem_open are in librt, not libc
            string[] librariesToTry = new[]
            {
                "librt.so.1",      // Real-time extensions (where shm_open actually is)
                "librt.so",        // Real-time extensions (generic)
                "libc.so.6",       // glibc on most Linux distributions (fallback)
                "libc.so",         // Generic libc
                "libc.musl-x86_64.so.1",  // musl libc on Alpine Linux (x64)
                "libc.musl-aarch64.so.1", // musl libc on Alpine Linux (ARM64)
                "libc",            // Let runtime figure it out
            };

            foreach (var libName in librariesToTry)
            {
                if (!NativeLibrary.TryLoad(libName, assembly, searchPath, out IntPtr handle)) continue;
                // Verify that critical POSIX functions exist in this library
                // This ensures we load the RIGHT library, not just ANY library
                if (NativeLibrary.TryGetExport(handle, "shm_open", out _) ||
                    NativeLibrary.TryGetExport(handle, "open", out _))  // At least basic functions
                {
                    return handle;
                }
                // If symbols not found, free this library and try next
                NativeLibrary.Free(handle);
            }

            // If all attempts fail, return IntPtr.Zero and let the runtime handle the error
            return IntPtr.Zero;
        }

        // File modes
        public const int S_IRUSR = 0x100; // Owner read
        public const int S_IWUSR = 0x080; // Owner write
        public const int S_IRGRP = 0x020; // Group read
        public const int S_IWGRP = 0x010; // Group write
        public const int S_IROTH = 0x004; // Others read
        public const int S_IWOTH = 0x002; // Others write
        
        // mmap protection flags
        public const int PROT_READ = 0x1;
        public const int PROT_WRITE = 0x2;
        
        // mmap flags
        public const int MAP_SHARED = 0x01;
        
        // Open flags
        public const int O_CREAT = 0x40;
        public const int O_EXCL = 0x80;
        public const int O_RDWR = 0x02;
        public const int O_RDONLY = 0x00;
        
        // Semaphore constants
        public const int SEM_FAILED = -1;

        // Shared memory functions
        [DllImport("libc", SetLastError = true)]
        public static extern int shm_open(string name, int oflag, int mode);

        [DllImport("libc", SetLastError = true)]
        public static extern int shm_unlink(string name);

        [DllImport("libc", SetLastError = true)]
        public static extern int ftruncate(int fd, long length);

        [DllImport("libc", SetLastError = true)]
        public static extern IntPtr mmap(IntPtr addr, ulong length, int prot, int flags, int fd, long offset);

        [DllImport("libc", SetLastError = true)]
        public static extern int munmap(IntPtr addr, ulong length);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);

        // Semaphore functions
        [DllImport("libc", SetLastError = true)]
        public static extern IntPtr sem_open(string name, int oflag, int mode, uint value);

        [DllImport("libc", SetLastError = true)]
        public static extern int sem_close(IntPtr sem);

        [DllImport("libc", SetLastError = true)]
        public static extern int sem_unlink(string name);

        [DllImport("libc", SetLastError = true)]
        public static extern int sem_wait(IntPtr sem);

        [DllImport("libc", SetLastError = true)]
        public static extern int sem_post(IntPtr sem);

        [DllImport("libc", SetLastError = true)]
        public static extern int sem_trywait(IntPtr sem);

        [DllImport("libc", SetLastError = true)]
        public static extern int sem_timedwait(IntPtr sem, ref Timespec abs_timeout);

        // Error handling
        [DllImport("libc")]
        public static extern int errno();

        [DllImport("libc")]
        public static extern IntPtr strerror(int errnum);

        // Helper structures
        [StructLayout(LayoutKind.Sequential)]
        public struct Timespec
        {
            public long tv_sec;   // seconds
            public long tv_nsec;  // nanoseconds
        }

        // File operations
        [DllImport("libc", SetLastError = true)]
        public static extern long lseek(int fd, long offset, int whence);
        
        [DllImport("libc", SetLastError = true)]
        public static extern int open(string pathname, int flags, int mode);
        
        [DllImport("libc", SetLastError = true)]
        public static extern int flock(int fd, int operation);
        
        [DllImport("libc", SetLastError = true)]
        public static extern int unlink(string pathname);
        
        // Seek constants
        public const int SEEK_SET = 0;
        public const int SEEK_CUR = 1;
        public const int SEEK_END = 2;

        // Helper methods
        public static string GetLastError()
        {
            int err = Marshal.GetLastWin32Error();
            IntPtr msgPtr = strerror(err);
            return Marshal.PtrToStringAnsi(msgPtr) ?? $"Unknown error {err}";
        }

        public static IntPtr MAP_FAILED => new IntPtr(-1);
    }
}