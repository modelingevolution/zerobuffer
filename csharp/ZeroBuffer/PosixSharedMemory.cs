using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZeroBuffer
{
    /// <summary>
    /// POSIX implementation of shared memory using shm_open/mmap
    /// </summary>
    internal sealed class PosixSharedMemory : ISharedMemory
    {
        private readonly string _name;
        private readonly long _size;
        private readonly bool _owner;
        private IntPtr _baseAddress = IntPtr.Zero;
        private int _fd = -1;
        private bool _disposed;

        public string Name => _name;
        public long Size => _size;

        private PosixSharedMemory(string name, long size, bool owner)
        {
            _name = name;
            _size = size;
            _owner = owner;
        }

        public PosixSharedMemory(string name, long size) : this(name, size, true)
        {
            try
            {
                // Create shared memory
                int mode = PosixInterop.S_IRUSR | PosixInterop.S_IWUSR | 
                          PosixInterop.S_IRGRP | PosixInterop.S_IWGRP;
                
                _fd = PosixInterop.shm_open(name, 
                    PosixInterop.O_CREAT | PosixInterop.O_EXCL | PosixInterop.O_RDWR, 
                    mode);
                
                if (_fd == -1)
                {
                    throw new InvalidOperationException($"Failed to create shared memory '{name}': {PosixInterop.GetLastError()}");
                }

                // Set size
                if (PosixInterop.ftruncate(_fd, size) == -1)
                {
                    throw new InvalidOperationException($"Failed to set shared memory size: {PosixInterop.GetLastError()}");
                }

                MapMemory();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static PosixSharedMemory OpenExisting(string name)
        {
            var shm = new PosixSharedMemory(name, 0, false);
            
            try
            {
                // Open existing shared memory
                shm._fd = PosixInterop.shm_open(name, PosixInterop.O_RDWR, 0);
                
                if (shm._fd == -1)
                {
                    var error = PosixInterop.GetLastError();
                    // ENOENT = 2 on most POSIX systems
                    if (error.Contains("No such file") || error.Contains("ENOENT"))
                    {
                        throw new BufferNotFoundException(name);
                    }
                    throw new InvalidOperationException($"Failed to open shared memory '{name}': {error}");
                }

                // Get size by seeking to end
                var size = PosixInterop.lseek(shm._fd, 0, PosixInterop.SEEK_END);
                if (size == -1)
                {
                    throw new InvalidOperationException($"Failed to get shared memory size: {PosixInterop.GetLastError()}");
                }
                
                // Reset position
                PosixInterop.lseek(shm._fd, 0, PosixInterop.SEEK_SET);
                
                // Update size
                typeof(PosixSharedMemory)
                    .GetField("_size", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(shm, size);

                shm.MapMemory();
                return shm;
            }
            catch
            {
                shm.Dispose();
                throw;
            }
        }

        private void MapMemory()
        {
            _baseAddress = PosixInterop.mmap(IntPtr.Zero, (ulong)_size,
                PosixInterop.PROT_READ | PosixInterop.PROT_WRITE,
                PosixInterop.MAP_SHARED, _fd, 0);

            if (_baseAddress == PosixInterop.MAP_FAILED)
            {
                throw new InvalidOperationException($"Failed to map shared memory: {PosixInterop.GetLastError()}");
            }
        }

        public unsafe ref T ReadRef<T>(long offset) where T : struct
        {
            ThrowIfDisposed();
            if (offset < 0 || offset + Marshal.SizeOf<T>() > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
                
            return ref Unsafe.AsRef<T>((void*)(_baseAddress + (int)offset));
        }

        public void Write<T>(long offset, in T value) where T : struct
        {
            ThrowIfDisposed();
            if (offset < 0 || offset + Marshal.SizeOf<T>() > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
                
            Marshal.StructureToPtr(value, _baseAddress + (int)offset, false);
        }

        public void ReadArray(long offset, byte[] buffer, int index, int count)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || offset + count > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (index < 0 || index + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
                
            Marshal.Copy(_baseAddress + (int)offset, buffer, index, count);
        }

        public void WriteArray(long offset, byte[] buffer, int index, int count)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || offset + count > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (index < 0 || index + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
                
            Marshal.Copy(buffer, index, _baseAddress + (int)offset, count);
        }
        
        public unsafe void WriteSpan(long offset, ReadOnlySpan<byte> data)
        {
            ThrowIfDisposed();
            if (offset < 0 || offset + data.Length > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
                
            var destination = new Span<byte>((void*)(_baseAddress + (int)offset), data.Length);
            data.CopyTo(destination);
        }

        public void Flush()
        {
            Thread.MemoryBarrier();
            // On POSIX, we could call msync here if needed
        }
        
        public unsafe byte* GetPointer(long offset)
        {
            ThrowIfDisposed();
            if (offset < 0 || offset >= _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
                
            return (byte*)(_baseAddress + (int)offset);
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

            if (_baseAddress != IntPtr.Zero && _baseAddress != PosixInterop.MAP_FAILED)
            {
                PosixInterop.munmap(_baseAddress, (ulong)_size);
            }

            if (_fd != -1)
            {
                PosixInterop.close(_fd);
                
                if (_owner)
                {
                    PosixInterop.shm_unlink(_name);
                }
            }
        }
        
        /// <summary>
        /// Removes shared memory by name (static method for cleanup)
        /// </summary>
        public static void Remove(string name)
        {
            try
            {
                PosixInterop.shm_unlink(name);
            }
            catch
            {
                // Ignore errors - resource might not exist
            }
        }
    }
}