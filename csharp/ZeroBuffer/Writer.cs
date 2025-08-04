using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZeroBuffer
{
    /// <summary>
    /// Cross-platform Writer implementation using abstracted shared memory and semaphores
    /// </summary>
    public sealed class Writer : IDisposable
    {
        private readonly string _name;
        private readonly ILogger<Writer> _logger;
        private readonly ISharedMemory _sharedMemory;
        private readonly ISemaphore _writeSemaphore;
        private readonly ISemaphore _readSemaphore;
        private readonly long _metadataOffset;
        private readonly long _payloadOffset;
        private bool _disposed;
        private bool _metadataWritten;
        private ulong _sequenceNumber = 1;

        public Writer(string name) : this(name, NullLogger<Writer>.Instance)
        {
        }

        public Writer(string name, ILogger<Writer> logger)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(logger);

            _name = name;
            _logger = logger;

            try
            {
                // Open shared memory first to check if buffer exists
                _sharedMemory = SharedMemoryFactory.Open(name);
                
                // Only try to open semaphores if shared memory exists
                _writeSemaphore = SemaphoreFactory.Open($"sem-w-{name}");
                _readSemaphore = SemaphoreFactory.Open($"sem-r-{name}");

                // Read OIEB to get layout - use ref for initial read-only access
                ref readonly var oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);
                
                // Calculate offsets
                _metadataOffset = (long)oiebRef.OperationSize;
                _payloadOffset = _metadataOffset + (long)oiebRef.MetadataSize;

                // Check if another writer is already connected
                if (oiebRef.WriterPid != 0 && ProcessExists(oiebRef.WriterPid))
                {
                    throw new WriterAlreadyConnectedException();
                }

                // Now read a mutable copy to set our PID
                var oieb = _sharedMemory.Read<OIEB>(0);
                oieb.WriterPid = (ulong)Environment.ProcessId;
                _sharedMemory.Write(0, oieb);
                _sharedMemory.Flush();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Get metadata buffer for zero-copy writing
        /// Returns a span where you can write metadata directly
        /// </summary>
        public unsafe Span<byte> GetMetadataBuffer(int size)
        {
            ThrowIfDisposed();

            if (_metadataWritten)
                throw new InvalidOperationException("Metadata already written");

            ref readonly var oieb = ref _sharedMemory.ReadRef<OIEB>(0);

            if ((ulong)size > oieb.MetadataSize)
                throw new ArgumentException("Metadata too large for buffer");

            // Get pointer to metadata area
            byte* metadataPtr = _sharedMemory.GetPointer(_metadataOffset);
            
            // Store size for commit
            _pendingMetadataSize = size;
            
            return new Span<byte>(metadataPtr, size);
        }
        
        private int _pendingMetadataSize;
        
        /// <summary>
        /// Commit metadata after writing to the buffer returned by GetMetadataBuffer
        /// </summary>
        public void CommitMetadata()
        {
            var oieb = _sharedMemory.Read<OIEB>(0);

            // Update OIEB
            oieb.MetadataWrittenBytes = (ulong)_pendingMetadataSize;
            oieb.MetadataFreeBytes = oieb.MetadataSize - (ulong)_pendingMetadataSize;
            _sharedMemory.Write(0, oieb);
            _sharedMemory.Flush();

            _metadataWritten = true;
        }

        /// <summary>
        /// Set metadata (convenience method that copies data)
        /// For zero-copy writing, use GetMetadataBuffer/CommitMetadata instead
        /// </summary>
        public void SetMetadata(ReadOnlySpan<byte> data)
        {
            var buffer = GetMetadataBuffer(data.Length);
            data.CopyTo(buffer);
            CommitMetadata();
        }

        /// <summary>
        /// Get a buffer for writing frame data directly (zero-copy write)
        /// Returns a span where you can write your data directly
        /// Call CommitFrame() after writing to complete the operation
        /// </summary>
        public unsafe Span<byte> GetFrameBuffer(int size, out ulong sequenceNumber)
        {
            ThrowIfDisposed();

            _logger.LogDebug("GetFrameBuffer called with size={Size}", size);

            // Check frame size
            long totalFrameSize = Marshal.SizeOf<FrameHeader>() + size;
            
            // Use ReadRef for initial checks to avoid copying
            ref readonly var oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);
            if ((ulong)totalFrameSize > oiebRef.PayloadSize)
            {
                _logger.LogError("Frame too large: {FrameSize} > {PayloadSize}", totalFrameSize, oiebRef.PayloadSize);
                throw new FrameTooLargeException();
            }

            // Wait for space
            while (true)
            {
                // Re-read the reference for each iteration
                oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);

                // Check if reader is alive or disconnected
                if (oiebRef.ReaderPid == 0)
                {
                    throw new ReaderDeadException(); // No reader connected
                }
                else if (!ProcessExists(oiebRef.ReaderPid))
                {
                    throw new ReaderDeadException(); // Reader process died
                }

                // Check for available space
                if (oiebRef.PayloadFreeBytes >= (ulong)totalFrameSize)
                    break;

                // Wait for reader to consume data
                if (!_readSemaphore.Wait(TimeSpan.FromSeconds(5)))
                {
                    // Timeout - check if reader died
                    if (oiebRef.ReaderPid == 0 || !ProcessExists(oiebRef.ReaderPid))
                    {
                        throw new ReaderDeadException();
                    }
                    throw new BufferFullException();
                }
            }
            
            // Now read a mutable copy for modifications
            var oieb = _sharedMemory.Read<OIEB>(0);

            _logger.LogTrace("OIEB state before write: WrittenCount={WrittenCount}, ReadCount={ReadCount}, WritePos={WritePos}, ReadPos={ReadPos}, FreeBytes={FreeBytes}",
                oieb.PayloadWrittenCount, oieb.PayloadReadCount, oieb.PayloadWritePos, oieb.PayloadReadPos, oieb.PayloadFreeBytes);

            // Calculate write position
            long writePos = _payloadOffset + (long)oieb.PayloadWritePos;
            long spaceToEnd = _payloadOffset + (long)oieb.PayloadSize - writePos;

            // Check if we need to wrap
            if (totalFrameSize > spaceToEnd)
            {
                // Write wrap marker (protocol: payload_size = 0)
                var wrapHeader = new FrameHeader
                {
                    PayloadSize = 0,
                    SequenceNumber = 0
                };

                _sharedMemory.Write(writePos, wrapHeader);

                // Account for the wasted space at the end
                oieb.PayloadFreeBytes -= (ulong)spaceToEnd;
                
                // Update position to beginning
                oieb.PayloadWritePos = 0;
                oieb.PayloadWrittenCount++;
                writePos = _payloadOffset;

                // Check space again after wrap
                if (oieb.PayloadFreeBytes < (ulong)totalFrameSize)
                {
                    throw new BufferFullException();
                }
            }

            // Write frame header
            sequenceNumber = _sequenceNumber++;
            var header = new FrameHeader
            {
                PayloadSize = (ulong)size,
                SequenceNumber = sequenceNumber
            };

            _sharedMemory.Write(writePos, header);

            // Get pointer to frame data area
            long dataPos = writePos + Marshal.SizeOf<FrameHeader>();
            byte* framePtr = _sharedMemory.GetPointer(dataPos);
            
            // Store write position for commit
            _pendingWritePos = dataPos;
            _pendingFrameSize = size;
            
            return new Span<byte>(framePtr, size);
        }
        
        private long _pendingWritePos;
        private int _pendingFrameSize;
        
        /// <summary>
        /// Commit the frame after writing to the buffer returned by GetFrameBuffer
        /// </summary>
        public void CommitFrame()
        {
            var oieb = _sharedMemory.Read<OIEB>(0);
            
            // Update write position
            oieb.PayloadWritePos = (ulong)((_pendingWritePos + _pendingFrameSize - _payloadOffset) % (long)oieb.PayloadSize);
            oieb.PayloadWrittenCount++;

            // Update free bytes
            long totalFrameSize = Marshal.SizeOf<FrameHeader>() + _pendingFrameSize;
            oieb.PayloadFreeBytes -= (ulong)totalFrameSize;

            _sharedMemory.Write(0, oieb);
            _sharedMemory.Flush();

            // Signal data available
            _writeSemaphore.Release();
        }

        /// <summary>
        /// Write a frame to the buffer (convenience method that copies data)
        /// For zero-copy writing, use GetFrameBuffer/CommitFrame instead
        /// </summary>
        public void WriteFrame(ReadOnlySpan<byte> data)
        {
            ThrowIfDisposed();

            // Check frame size
            long totalFrameSize = Marshal.SizeOf<FrameHeader>() + data.Length;
            ref readonly var oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);

            if ((ulong)totalFrameSize > oiebRef.PayloadSize)
                throw new FrameTooLargeException();

            // Wait for space - need mutable copy for the loop
            var oieb = _sharedMemory.Read<OIEB>(0);
            while (true)
            {
                oieb = _sharedMemory.Read<OIEB>(0);

                // Check if reader is alive or disconnected
                if (oieb.ReaderPid == 0)
                {
                    throw new ReaderDeadException(); // No reader connected
                }
                else if (!ProcessExists(oieb.ReaderPid))
                {
                    throw new ReaderDeadException(); // Reader process died
                }

                // Check for available space
                if (oieb.PayloadFreeBytes >= (ulong)totalFrameSize)
                    break;

                // Wait for reader to consume data
                if (!_readSemaphore.Wait(TimeSpan.FromSeconds(5)))
                {
                    // Timeout - check if reader died
                    if (oieb.ReaderPid == 0 || !ProcessExists(oieb.ReaderPid))
                    {
                        throw new ReaderDeadException();
                    }
                    throw new BufferFullException();
                }
            }

            // Calculate write position
            long writePos = _payloadOffset + (long)oieb.PayloadWritePos;
            long spaceToEnd = _payloadOffset + (long)oieb.PayloadSize - writePos;

            // Check if we need to wrap
            if (totalFrameSize > spaceToEnd)
            {
                // Write wrap marker (protocol: payload_size = 0)
                var wrapHeader = new FrameHeader
                {
                    PayloadSize = 0,
                    SequenceNumber = 0
                };

                _sharedMemory.Write(writePos, wrapHeader);

                // Account for the wasted space at the end
                oieb.PayloadFreeBytes -= (ulong)spaceToEnd;
                
                // Update position to beginning
                oieb.PayloadWritePos = 0;
                oieb.PayloadWrittenCount++;
                writePos = _payloadOffset;

                // Check space again after wrap
                if (oieb.PayloadFreeBytes < (ulong)totalFrameSize)
                {
                    throw new BufferFullException();
                }
            }

            // Write frame header
            var header = new FrameHeader
            {
                PayloadSize = (ulong)data.Length,
                SequenceNumber = _sequenceNumber++
            };

            _sharedMemory.Write(writePos, header);

            // Write frame data
            long dataPos = writePos + Marshal.SizeOf<FrameHeader>();
            _sharedMemory.WriteSpan(dataPos, data);

            // Update write position
            oieb.PayloadWritePos = (ulong)((dataPos + data.Length - _payloadOffset) % (long)oieb.PayloadSize);
            oieb.PayloadWrittenCount++;

            // Update free bytes
            oieb.PayloadFreeBytes -= (ulong)totalFrameSize;

            _sharedMemory.Write(0, oieb);
            _sharedMemory.Flush();

            // Signal data available
            _writeSemaphore.Release();
        }

        /// <summary>
        /// Check if reader is connected
        /// </summary>
        public bool IsReaderConnected()
        {
            if (_disposed)
                return false;

            // Use ReadRef to avoid copying OIEB
            ref readonly var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
            return oieb.ReaderPid != 0 && ProcessExists(oieb.ReaderPid);
        }

        public string Name => _name;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long CalculateUsedBytes(ulong writePos, ulong readPos, ulong bufferSize)
        {
            return writePos >= readPos 
                ? (long)(writePos - readPos) 
                : (long)(bufferSize - readPos + writePos);
        }

        private static bool ProcessExists(ulong pid)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
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

            // Clear writer PID
            try
            {
                var oieb = _sharedMemory.Read<OIEB>(0);
                oieb.WriterPid = 0;
                _sharedMemory.Write(0, oieb);
                _sharedMemory.Flush();
            }
            catch { }

            _sharedMemory?.Dispose();
            _writeSemaphore?.Dispose();
            _readSemaphore?.Dispose();
        }
    }
}