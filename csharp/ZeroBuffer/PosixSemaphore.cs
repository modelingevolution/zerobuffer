using System;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// POSIX implementation of named semaphore
    /// </summary>
    internal sealed class PosixSemaphore : ISemaphore
    {
        // SEM_FAILED is (sem_t*)-1 on POSIX systems
        private static readonly IntPtr SEM_FAILED = new IntPtr(-1);

        private readonly string _name;
        private readonly bool _owner;
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed;

        private PosixSemaphore(string name, bool owner)
        {
            _name = name;
            _owner = owner;
        }

        public PosixSemaphore(string name, int initialCount) : this(name, true)
        {
            // Ensure P/Invoke resolver is initialized before any native calls
            PosixInterop.EnsureInitialized();

            // POSIX semaphore names must start with /
            var semName = name.StartsWith('/') ? name : $"/{name}";

            int mode = PosixInterop.S_IRUSR | PosixInterop.S_IWUSR |
                      PosixInterop.S_IRGRP | PosixInterop.S_IWGRP |
                      PosixInterop.S_IROTH | PosixInterop.S_IWOTH;  // 0666 - read/write for all
            
            _handle = PosixInterop.sem_open(semName,
                PosixInterop.O_CREAT | PosixInterop.O_EXCL,
                mode, (uint)initialCount);

            if (_handle == SEM_FAILED)
            {
                _handle = IntPtr.Zero; // Reset to safe value for Dispose()
                throw new InvalidOperationException($"Failed to create semaphore '{semName}': {PosixInterop.GetLastError()}");
            }
        }

        public static PosixSemaphore OpenExisting(string name)
        {
            // Ensure P/Invoke resolver is initialized before any native calls
            PosixInterop.EnsureInitialized();

            var sem = new PosixSemaphore(name, false);

            // POSIX semaphore names must start with /
            var semName = name.StartsWith('/') ? name : $"/{name}";
            Console.WriteLine($"[PosixSemaphore] OpenExisting: '{semName}'");
            Console.Out.Flush();

            sem._handle = PosixInterop.sem_open(semName, 0, 0, 0);
            Console.WriteLine($"[PosixSemaphore] sem_open returned handle: 0x{sem._handle:X}");
            Console.Out.Flush();

            if (sem._handle == SEM_FAILED)
            {
                Console.WriteLine($"[PosixSemaphore] sem_open FAILED (SEM_FAILED = -1)");
                Console.Out.Flush();
                sem._handle = IntPtr.Zero; // Reset to safe value for Dispose()
                var error = PosixInterop.GetLastError();
                // ENOENT = 2 on most POSIX systems
                if (error.Contains("No such file") || error.Contains("ENOENT"))
                {
                    throw new BufferNotFoundException(name, true);
                }
                throw new InvalidOperationException($"Failed to open semaphore '{semName}': {error}");
            }

            Console.WriteLine($"[PosixSemaphore] OpenExisting succeeded: '{semName}' -> 0x{sem._handle:X}");
            Console.Out.Flush();
            return sem;
        }

        public bool Wait(TimeSpan? timeout = null)
        {
            ThrowIfDisposed();
            
            if (timeout == null || timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                // Wait indefinitely
                return PosixInterop.sem_wait(_handle) == 0;
            }
            
            if (timeout.Value <= TimeSpan.Zero)
            {
                // Try without blocking
                return PosixInterop.sem_trywait(_handle) == 0;
            }
            
            // Wait with timeout
            var deadline = DateTime.UtcNow + timeout.Value;
            var ts = new PosixInterop.Timespec
            {
                tv_sec = (long)deadline.ToUnixTimeSeconds(),
                tv_nsec = (deadline.Ticks % TimeSpan.TicksPerSecond) * 100
            };
            
            int result = PosixInterop.sem_timedwait(_handle, ref ts);
            return result == 0;
        }

        public void Release()
        {
            ThrowIfDisposed();
            Console.WriteLine($"[PosixSemaphore] Release: calling sem_post on handle 0x{_handle:X} (name: {_name})");
            Console.Out.Flush();

            int result = PosixInterop.sem_post(_handle);
            Console.WriteLine($"[PosixSemaphore] sem_post returned: {result}");
            Console.Out.Flush();

            if (result != 0)
            {
                var error = PosixInterop.GetLastError();
                Console.WriteLine($"[PosixSemaphore] sem_post FAILED: {error}");
                Console.Out.Flush();
                throw new InvalidOperationException($"Failed to release semaphore: {error}");
            }
        }

        public bool TryWait()
        {
            ThrowIfDisposed();
            return PosixInterop.sem_trywait(_handle) == 0;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private bool IsHandleValid => _handle != IntPtr.Zero && _handle != SEM_FAILED;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (IsHandleValid)
            {
                PosixInterop.sem_close(_handle);

                if (_owner)
                {
                    var semName = _name.StartsWith('/') ? _name : $"/{_name}";
                    PosixInterop.sem_unlink(semName);
                }
            }
        }
        
        /// <summary>
        /// Removes semaphore by name (static method for cleanup)
        /// </summary>
        public static void Remove(string name)
        {
            // Ensure P/Invoke resolver is initialized before any native calls
            PosixInterop.EnsureInitialized();

            try
            {
                var semName = name.StartsWith('/') ? name : $"/{name}";
                PosixInterop.sem_unlink(semName);
            }
            catch
            {
                // Ignore errors - resource might not exist
            }
        }
    }
    
    internal static class DateTimeExtensions
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        public static long ToUnixTimeSeconds(this DateTime dateTime)
        {
            return (long)(dateTime.ToUniversalTime() - UnixEpoch).TotalSeconds;
        }
    }
}