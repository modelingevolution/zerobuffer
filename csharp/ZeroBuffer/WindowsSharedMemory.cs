using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZeroBuffer
{
    /// <summary>
    /// Windows implementation of shared memory using MemoryMappedFile
    /// </summary>
    internal sealed unsafe class WindowsSharedMemory : ISharedMemory
    {
        private readonly string _name;
        private readonly long _size;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly byte* _basePointer;
        private bool _disposed;

        public string Name => _name;
        public long Size => _size;

        public WindowsSharedMemory(string name, long size)
        {
            _name = name;
            _size = size;
            _mmf = MemoryMappedFile.CreateNew(name, size);
            _accessor = _mmf.CreateViewAccessor();
            
            // Acquire pointer once during construction
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _basePointer = ptr;
        }

        public static WindowsSharedMemory OpenExisting(string name)
        {
            var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
            var accessor = mmf.CreateViewAccessor();
            
            // Get the capacity from the accessor
            var size = accessor.Capacity;
            
            return new WindowsSharedMemory(name, size, mmf, accessor);
        }

        private WindowsSharedMemory(string name, long size, MemoryMappedFile mmf, MemoryMappedViewAccessor accessor)
        {
            _name = name;
            _size = size;
            _mmf = mmf;
            _accessor = accessor;
            
            // Acquire pointer once during construction
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _basePointer = ptr;
        }

        public ref T ReadRef<T>(long offset) where T : struct
        {
            ThrowIfDisposed();
            if (offset < 0 || offset + Unsafe.SizeOf<T>() > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
                
            // Use the pre-acquired base pointer
            return ref Unsafe.AsRef<T>((void*)(_basePointer + offset));
        }

        public void Write<T>(long offset, in T value) where T : struct
        {
            ThrowIfDisposed();
            _accessor.Write(offset, ref Unsafe.AsRef(in value));
        }

        public void ReadArray(long offset, byte[] buffer, int index, int count)
        {
            ThrowIfDisposed();
            _accessor.ReadArray(offset, buffer, index, count);
        }

        public void WriteArray(long offset, byte[] buffer, int index, int count)
        {
            ThrowIfDisposed();
            _accessor.WriteArray(offset, buffer, index, count);
        }
        
        public unsafe void WriteSpan(long offset, ReadOnlySpan<byte> data)
        {
            ThrowIfDisposed();
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            try
            {
                var destination = new Span<byte>(ptr + offset, data.Length);
                data.CopyTo(destination);
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        public void Flush()
        {
            ThrowIfDisposed();
            _accessor.Flush();
            Thread.MemoryBarrier();
        }
        
        public unsafe byte* GetPointer(long offset)
        {
            ThrowIfDisposed();
            if (offset < 0 || offset >= _size)
                throw new ArgumentOutOfRangeException(nameof(offset));
                
            byte* basePtr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            return basePtr + offset;
            // Note: Need to be careful about ReleasePointer - caller must manage lifetime
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
            
            // Release the pointer before disposing accessor
            if (_basePointer != null)
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }
}