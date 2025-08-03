using System;
using System.Threading;

namespace ZeroBuffer
{
    /// <summary>
    /// Windows implementation of named semaphore using System.Threading.Semaphore
    /// </summary>
    internal sealed class WindowsSemaphore : ISemaphore
    {
        private readonly Semaphore _semaphore;
        private bool _disposed;

        public WindowsSemaphore(string name, int initialCount)
        {
            _semaphore = new Semaphore(initialCount, int.MaxValue, name);
        }

        public static WindowsSemaphore OpenExisting(string name)
        {
            if (!Semaphore.TryOpenExisting(name, out var semaphore))
            {
                throw new InvalidOperationException($"Semaphore '{name}' not found");
            }
            
            return new WindowsSemaphore(semaphore);
        }

        private WindowsSemaphore(Semaphore semaphore)
        {
            _semaphore = semaphore;
        }

        public bool Wait(TimeSpan? timeout = null)
        {
            ThrowIfDisposed();
            
            if (timeout == null || timeout == Timeout.InfiniteTimeSpan)
            {
                _semaphore.WaitOne();
                return true;
            }
            
            return _semaphore.WaitOne(timeout.Value);
        }

        public void Release()
        {
            ThrowIfDisposed();
            _semaphore.Release();
        }

        public bool TryWait()
        {
            ThrowIfDisposed();
            return _semaphore.WaitOne(0);
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
            _semaphore?.Dispose();
        }
    }
}