using System;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// POSIX implementation of named semaphore
    /// </summary>
    internal sealed class PosixSemaphore : ISemaphore
    {
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
            // POSIX semaphore names must start with /
            var semName = name.StartsWith('/') ? name : $"/{name}";
            
            int mode = PosixInterop.S_IRUSR | PosixInterop.S_IWUSR | 
                      PosixInterop.S_IRGRP | PosixInterop.S_IWGRP;
            
            _handle = PosixInterop.sem_open(semName,
                PosixInterop.O_CREAT | PosixInterop.O_EXCL,
                mode, (uint)initialCount);
                
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create semaphore '{semName}': {PosixInterop.GetLastError()}");
            }
        }

        public static PosixSemaphore OpenExisting(string name)
        {
            var sem = new PosixSemaphore(name, false);
            
            // POSIX semaphore names must start with /
            var semName = name.StartsWith('/') ? name : $"/{name}";
            
            sem._handle = PosixInterop.sem_open(semName, 0, 0, 0);
            
            if (sem._handle == IntPtr.Zero)
            {
                var error = PosixInterop.GetLastError();
                // ENOENT = 2 on most POSIX systems
                if (error.Contains("No such file") || error.Contains("ENOENT"))
                {
                    throw new BufferNotFoundException(name, true);
                }
                throw new InvalidOperationException($"Failed to open semaphore '{semName}': {error}");
            }
            
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
            
            if (PosixInterop.sem_post(_handle) != 0)
            {
                throw new InvalidOperationException($"Failed to release semaphore: {PosixInterop.GetLastError()}");
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

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_handle != IntPtr.Zero)
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