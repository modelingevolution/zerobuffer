using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// Wrapper around .NET's built-in Semaphore for cross-process synchronization
    /// .NET's Semaphore already supports named semaphores for IPC
    /// </summary>
    internal sealed class CrossProcessSemaphore : IDisposable
    {
        private readonly Semaphore _semaphore;
        private bool _disposed;

        private CrossProcessSemaphore(Semaphore semaphore)
        {
            _semaphore = semaphore;
        }

        /// <summary>
        /// Create a new named semaphore using .NET's built-in Semaphore
        /// </summary>
        public static CrossProcessSemaphore Create(string name, int initialCount, int maximumCount)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            try
            {
                // .NET's Semaphore handles platform differences internally
                // On Windows: uses named kernel objects
                // On Linux/Mac: uses POSIX semaphores via .NET runtime
                var semaphore = new Semaphore(initialCount, maximumCount, name, out bool createdNew);
                
                if (!createdNew)
                {
                    semaphore.Dispose();
                    throw new ZeroBufferException($"Semaphore '{name}' already exists");
                }

                return new CrossProcessSemaphore(semaphore);
            }
            catch (Exception ex) when (ex is not ZeroBufferException)
            {
                throw new ZeroBufferException($"Failed to create semaphore '{name}'", ex);
            }
        }

        /// <summary>
        /// Open an existing named semaphore using .NET's built-in Semaphore.OpenExisting
        /// </summary>
        public static CrossProcessSemaphore Open(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            try
            {
                var semaphore = Semaphore.OpenExisting(name);
                return new CrossProcessSemaphore(semaphore);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                throw new ZeroBufferException($"Semaphore '{name}' not found");
            }
            catch (Exception ex)
            {
                throw new ZeroBufferException($"Failed to open semaphore '{name}'", ex);
            }
        }

        /// <summary>
        /// Wait for the semaphore with timeout
        /// </summary>
        public bool Wait(TimeSpan timeout)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                return _semaphore.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // The process that owned the semaphore died
                // This is actually useful information - we got the semaphore
                return true;
            }
        }

        /// <summary>
        /// Release the semaphore
        /// </summary>
        public void Release()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _semaphore.Release();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _semaphore?.Dispose();
        }
    }
}