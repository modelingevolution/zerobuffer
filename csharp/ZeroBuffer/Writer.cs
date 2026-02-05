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
        private TimeSpan _writeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the timeout for write operations when the buffer is full.
        /// Default is 5 seconds.
        /// </summary>
        public TimeSpan WriteTimeout 
        { 
            get => _writeTimeout;
            set 
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value), "Write timeout must be positive");
                _writeTimeout = value;
            }
        }

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
                
                // Verify OIEB size and version compatibility
                if (oiebRef.OiebSize != 128)
                {
                    throw new InvalidOperationException("Invalid OIEB size - expected 128 for v1.x.x");
                }
                
                var currentVersion = new ProtocolVersion(1, 0, 0);
                if (!oiebRef.Version.IsCompatibleWith(currentVersion))
                {
                    throw new InvalidOperationException("Incompatible protocol version");
                }
                
                // Calculate offsets (OIEB is always 128 bytes)
                _metadataOffset = 128;  // sizeof(OIEB)
                _payloadOffset = _metadataOffset + (long)oiebRef.MetadataSize;

                // Check if another writer is already connected
                if (oiebRef.WriterPid != 0 && ProcessExists(oiebRef.WriterPid))
                {
                    throw new WriterAlreadyConnectedException();
                }

                // Get a reference to set our PID directly
                ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
                oieb.WriterPid = (ulong)Environment.ProcessId;
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

            // Total size includes 8-byte size prefix
            ulong totalSize = (ulong)size + sizeof(ulong);
            if (totalSize > oieb.MetadataSize)
                throw new ArgumentException("Metadata too large for buffer");

            // Get pointer to metadata area
            byte* metadataPtr = _sharedMemory.GetPointer(_metadataOffset);
            
            // Write size prefix (8 bytes)
            *(ulong*)metadataPtr = (ulong)size;
            
            // Store size for commit
            _pendingMetadataSize = (int)totalSize;
            
            // Return buffer starting after the size prefix
            return new Span<byte>(metadataPtr + sizeof(ulong), size);
        }
        
        private int _pendingMetadataSize;
        
        /// <summary>
        /// Commit metadata after writing to the buffer returned by GetMetadataBuffer
        /// </summary>
        public void CommitMetadata()
        {
            ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);

            // Update OIEB with total size (including 8-byte prefix)
            oieb.MetadataWrittenBytes = (ulong)_pendingMetadataSize;
            oieb.MetadataFreeBytes = oieb.MetadataSize - (ulong)_pendingMetadataSize;
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
        /// Call CommitFrame() after writing to complete the operation, can throw ReaderDeadException, FrameTooLargeException, BufferFullException
        /// </summary>
        public unsafe Span<byte> GetFrameBuffer(int size, out ulong sequenceNumber)
        {
            ThrowIfDisposed();

            _logger.LogDebug("GetFrameBuffer called with size={Size}", size);

            // Check frame size
            long totalFrameSize = FrameHeader.SIZE + size;
            
            // Use ReadRef for initial checks to avoid copying
            ref readonly var oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);
            if(oiebRef.ReaderPid == 0)
                throw new ReaderDeadException();

            if ((ulong)totalFrameSize > oiebRef.PayloadSize)
            {
                _logger.LogError("Frame too large: {FrameSize} > {PayloadSize}", totalFrameSize, oiebRef.PayloadSize);
                throw new FrameTooLargeException();
            }

            // Wait for space
            while (true)
            {
                // First check current state
                oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);
                
                // Calculate required space including potential wrap-around waste
                long spaceToEndCheck = (long)oiebRef.PayloadSize - (long)oiebRef.PayloadWritePos;
                ulong requiredSpace = (spaceToEndCheck >= totalFrameSize)
                    ? (ulong)totalFrameSize                                      // no wrap needed
                    : (ulong)spaceToEndCheck + (ulong)totalFrameSize;            // wrap: waste at end + frame at beginning

                if (oiebRef.PayloadFreeBytes >= requiredSpace)
                    break;

                // Need to wait for space - wait on semaphore
                if (!_readSemaphore.Wait(TimeSpan.FromSeconds(5)))
                {
                    // Timeout - check if reader died
                    oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);
                    if (oiebRef.ReaderPid == 0 || !ProcessExists(oiebRef.ReaderPid))
                    {
                        throw new ReaderDeadException();
                    }
                    throw new BufferFullException();
                }
                // Loop back to check space again after semaphore signal
            }
            
            // Get a reference for direct modifications
            ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);

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

                // Account for the wasted space at the end - atomic to prevent lost updates
                Interlocked.Add(ref Unsafe.As<ulong, long>(ref oieb.PayloadFreeBytes), -(long)spaceToEnd);

                // Update position to beginning
                oieb.PayloadWritePos = 0;
                oieb.PayloadWrittenCount++;
                writePos = _payloadOffset;

                // Check space again after wrap
                if ((ulong)Interlocked.Read(ref Unsafe.As<ulong, long>(ref oieb.PayloadFreeBytes)) < (ulong)totalFrameSize)
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
            long dataPos = writePos + FrameHeader.SIZE;
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
            ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);

            // Update write position directly in shared memory
            oieb.PayloadWritePos = (ulong)((_pendingWritePos + _pendingFrameSize - _payloadOffset) % (long)oieb.PayloadSize);
            oieb.PayloadWrittenCount++;

            // Update free bytes - atomic to prevent lost updates when reader adds concurrently
            long totalFrameSize = FrameHeader.SIZE + _pendingFrameSize;
            Interlocked.Add(ref Unsafe.As<ulong, long>(ref oieb.PayloadFreeBytes), -totalFrameSize);
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
            long totalFrameSize = FrameHeader.SIZE + data.Length;
            ref readonly var oiebRef = ref _sharedMemory.ReadRef<OIEB>(0);

            if ((ulong)totalFrameSize > oiebRef.PayloadSize)
                throw new FrameTooLargeException();

            // Wait for space
            ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
            while (true)
            {
                // Check if reader hasn't exited gracefully
                if (oieb.ReaderPid == 0)
                    throw new ReaderDeadException();
                
                // Calculate required space including potential wrap-around waste
                long spaceToEndCheck = (long)oieb.PayloadSize - (long)oieb.PayloadWritePos;
                ulong requiredSpace = (spaceToEndCheck >= totalFrameSize)
                    ? (ulong)totalFrameSize                                      // no wrap needed
                    : (ulong)spaceToEndCheck + (ulong)totalFrameSize;            // wrap: waste at end + frame at beginning

                if (oieb.PayloadFreeBytes >= requiredSpace)
                    break;

                // Need to wait for space - wait on semaphore
                if (_readSemaphore.Wait(_writeTimeout)) continue;

                if (oieb.ReaderPid == 0 || !ProcessExists(oieb.ReaderPid))
                {
                    throw new ReaderDeadException();
                }
                throw new BufferFullException();

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

                // Account for the wasted space at the end - atomic to prevent lost updates
                Interlocked.Add(ref Unsafe.As<ulong, long>(ref oieb.PayloadFreeBytes), -(long)spaceToEnd);

                // Update position to beginning
                oieb.PayloadWritePos = 0;
                oieb.PayloadWrittenCount++;
                writePos = _payloadOffset;

                // Check space again after wrap
                if ((ulong)Interlocked.Read(ref Unsafe.As<ulong, long>(ref oieb.PayloadFreeBytes)) < (ulong)totalFrameSize)
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
            long dataPos = writePos + FrameHeader.SIZE;
            _sharedMemory.WriteSpan(dataPos, data);

            // Now get a reference to update the OIEB in shared memory
            ref var oiebUpdate = ref _sharedMemory.ReadRef<OIEB>(0);

            // Update write position
            oiebUpdate.PayloadWritePos = (ulong)((dataPos + data.Length - _payloadOffset) % (long)oieb.PayloadSize);
            oiebUpdate.PayloadWrittenCount = oieb.PayloadWrittenCount + 1;

            // Update free bytes - atomic to prevent lost updates when reader adds concurrently
            Interlocked.Add(ref Unsafe.As<ulong, long>(ref oiebUpdate.PayloadFreeBytes), -(long)totalFrameSize);
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
                ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
                oieb.WriterPid = 0;
                _sharedMemory.Flush();
            }
            catch { }

            _sharedMemory?.Dispose();
            _writeSemaphore?.Dispose();
            _readSemaphore?.Dispose();
        }
        
        /// <summary>
        /// Get the current OIEB state (for testing purposes)
        /// </summary>
        internal unsafe OIEB GetOIEB()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Writer));
                
            return _sharedMemory.ReadRef<OIEB>(0);
        }
    }
}