using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZeroBuffer
{
    /// <summary>
    /// Cross-platform Reader implementation using abstracted shared memory and semaphores
    /// </summary>
    public sealed class Reader : IDisposable
    {
        private readonly string _name;
        private readonly BufferConfig _config;
        private readonly ILogger<Reader> _logger;
        private readonly IFileLock _lock = null!;
        private readonly ISharedMemory _sharedMemory = null!;
        private readonly ISemaphore _writeSemaphore = null!;
        private readonly ISemaphore _readSemaphore = null!;
        private readonly long _metadataOffset;
        private readonly long _payloadOffset;
        //private readonly FrameCallback _frameDisposeCallback = null!;
        private bool _disposed;

        public Reader(string name, BufferConfig config) : this(name, config, NullLogger<Reader>.Instance)
        {
        }

        public Reader(string name, BufferConfig config, ILogger<Reader> logger)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(logger);

            _name = name;
            _config = config;
            _logger = logger;

            // Create lock file path
            string lockPath = GetLockFilePath(name);

            // Try to clean up stale resources before creating
            CleanupStaleResources();

            // Calculate aligned sizes
            int oiebSize = AlignToBlockBoundary(OIEB.SIZE);
            int metadataSize = AlignToBlockBoundary(config.MetadataSize);
            int payloadSize = AlignToBlockBoundary(config.PayloadSize);

            long totalSize = oiebSize + metadataSize + payloadSize;

            // Set offsets
            _metadataOffset = oiebSize;
            _payloadOffset = oiebSize + metadataSize;
            
            

            bool created = false;
            int retryCount = 0;
            const int maxRetries = 2;

            while (!created && retryCount < maxRetries)
            {
                try
                {
                    // Create lock file first
                    _lock = FileLockFactory.Create(lockPath);
                    // Try to create shared memory
                    _sharedMemory = SharedMemoryFactory.Create(name, totalSize);

                    // Initialize OIEB
                    var oieb = new OIEB
                    {
                        OiebSize = 128,  // Always 128 for v1.x.x
                        Version = new ProtocolVersion(1, 0, 0),  // Version 1.0.0
                        MetadataSize = (ulong)metadataSize,
                        MetadataFreeBytes = (ulong)metadataSize,
                        MetadataWrittenBytes = 0,
                        PayloadSize = (ulong)payloadSize,
                        PayloadFreeBytes = (ulong)payloadSize,
                        PayloadWritePos = 0,
                        PayloadReadPos = 0,
                        PayloadWrittenCount = 0,
                        PayloadReadCount = 0,
                        WriterPid = 0,
                        ReaderPid = (ulong)Environment.ProcessId
                    };

                    _sharedMemory.Write(0, oieb);

                    // Create semaphores
                    _writeSemaphore = SemaphoreFactory.Create($"sem-w-{name}", 0);
                    _readSemaphore = SemaphoreFactory.Create($"sem-r-{name}", 0);

                    created = true;
                }
                catch when (retryCount < maxRetries - 1)
                {
                    // First attempt failed - try to clean up stale resources
                    retryCount++;

                    // Clean up any partial resources we created
                    try { _lock?.Dispose(); } catch { }
                    try { _sharedMemory?.Dispose(); } catch { }
                    try { _writeSemaphore?.Dispose(); } catch { }
                    try { _readSemaphore?.Dispose(); } catch { }
                    _lock = null!;
                    _sharedMemory = null!;
                    _writeSemaphore = null!;
                    _readSemaphore = null!;

                    // Check if there are stale resources from a dead process
                    bool shouldCleanup = false;
                    try
                    {
                        using var existingMem = SharedMemoryFactory.Open(name);
                        var existingOieb = existingMem.ReadRef<OIEB>(0);

                        // Check if the existing reader/writer processes are dead
                        if (existingOieb.ReaderPid != 0 && !ProcessExists(existingOieb.ReaderPid))
                            shouldCleanup = true;
                        if (existingOieb.WriterPid != 0 && !ProcessExists(existingOieb.WriterPid))
                            shouldCleanup = true;
                    }
                    catch
                    {
                        // Failed to open existing memory - assume it's corrupted
                        shouldCleanup = true;
                    }

                    if (shouldCleanup)
                    {
                        // Remove all stale resources
                        try
                        {
                            SharedMemoryFactory.Remove(name);
                            SemaphoreFactory.Remove($"sem-w-{name}");
                            SemaphoreFactory.Remove($"sem-r-{name}");
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }

                        // Small delay before retry
                        Thread.Sleep(100);
                    }
                    else
                    {
                        // Resources are in use by live processes
                        throw new ReaderAlreadyConnectedException();
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
        }

        private static string GetLockFilePath(string name)
        {
            // Use a platform-appropriate temp directory for lock files
            string tempDir = Path.GetTempPath();
            string lockDir = Path.Combine(tempDir, "zerobuffer", "locks");
            return Path.Combine(lockDir, $"{name}.lock");
        }

        private void CleanupStaleResources()
        {
            // Get the lock directory
            string tempDir = Path.GetTempPath();
            string lockDir = Path.Combine(tempDir, "zerobuffer", "locks");

            // Create directory if it doesn't exist
            try
            {
                Directory.CreateDirectory(lockDir);
            }
            catch
            {
                // Ignore errors creating directory
                return;
            }

            // Scan all lock files in the directory
            try
            {
                foreach (var lockFile in Directory.GetFiles(lockDir, "*.lock"))
                {
                    // Try to remove stale lock
                    if (!FileLockFactory.TryRemoveStale(lockFile)) continue;

                    // We successfully removed a stale lock, clean up associated resources
                    string bufferName = Path.GetFileNameWithoutExtension(lockFile);

                    try
                    {
                        // Try to open the shared memory to check if it's orphaned
                        using var shm = SharedMemoryFactory.Open(bufferName);
                        var oieb = shm.ReadRef<OIEB>(0);

                        // Check if both reader and writer are dead
                        bool readerDead = (oieb.ReaderPid == 0) || !ProcessExists(oieb.ReaderPid);
                        bool writerDead = (oieb.WriterPid == 0) || !ProcessExists(oieb.WriterPid);

                        if (readerDead && writerDead)
                        {
                            // Both processes are dead, safe to clean up
                            // First dispose our handle to the shared memory
                            shm.Dispose();

                            // Now remove all resources
                            SharedMemoryFactory.Remove(bufferName);
                            SemaphoreFactory.Remove($"sem-w-{bufferName}");
                            SemaphoreFactory.Remove($"sem-r-{bufferName}");
                        }
                    }
                    catch
                    {
                        // If we can't open shared memory, clean up anyway since lock was stale
                        try
                        {
                            SharedMemoryFactory.Remove(bufferName);
                            SemaphoreFactory.Remove($"sem-w-{bufferName}");
                            SemaphoreFactory.Remove($"sem-r-{bufferName}");
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors during directory scan
            }
        }

        
        /// <summary>
        /// Get metadata - zero-copy access via ReadOnlySpan
        /// </summary>
        public unsafe ReadOnlySpan<byte> GetMetadata()
        {
            ThrowIfDisposed();

            ref readonly var oieb = ref _sharedMemory.ReadRef<OIEB>(0);

            if (oieb.MetadataWrittenBytes == 0)
                return ReadOnlySpan<byte>.Empty;

            // Return a span directly over the shared memory - true zero-copy
            byte* metadataPtr = _sharedMemory.GetPointer(_metadataOffset);
            return new ReadOnlySpan<byte>(metadataPtr, (int)oieb.MetadataWrittenBytes);
        }

        /// <summary>
        /// Read a frame from the buffer with RAII semantics.
        /// The returned Frame should be used with 'using' to ensure proper disposal.
        /// </summary>
        public Frame ReadFrame(TimeSpan? timeout = null)
        {
            ThrowIfDisposed();

            var waitTime = timeout ?? TimeSpan.FromSeconds(5);
            _logger.LogDebug("ReadFrame called with timeout {Timeout}ms", waitTime.TotalMilliseconds);

            while (true)
            {
                // Wait for data signal FIRST (following the protocol correctly)
                _logger.LogTrace("Waiting on write semaphore for data signal");
                if (!_writeSemaphore.Wait(waitTime))
                {
                    // Timeout - check if writer is dead
                    ref readonly var oiebCheck = ref _sharedMemory.ReadRef<OIEB>(0);
                    if (oiebCheck.WriterPid == 0 || !ProcessExists(oiebCheck.WriterPid))
                    {
                        _logger.LogWarning("Writer process {WriterPid} is dead", oiebCheck.WriterPid);
                        throw new WriterDeadException();
                    }

                    _logger.LogDebug("Timeout waiting for frame");
                    return Frame.Invalid;
                }

                // Semaphore was signaled - data should be available
                // Use ref to access OIEB directly (no copy needed)
                ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);

                if(oieb.WriterPid == 0) // This is quick check to ensure writer hasn't disconnected gracefully.
                    throw new WriterDeadException();

                _logger.LogTrace("OIEB state after semaphore: WrittenCount={WrittenCount}, ReadCount={ReadCount}, WritePos={WritePos}, ReadPos={ReadPos}, FreeBytes={FreeBytes}, PayloadSize={PayloadSize}",
                    oieb.PayloadWrittenCount, oieb.PayloadReadCount, oieb.PayloadWritePos, oieb.PayloadReadPos, oieb.PayloadFreeBytes, oieb.PayloadSize);


                // Read frame header
                long readPos = _payloadOffset + (long)oieb.PayloadReadPos;
                ref readonly var header = ref _sharedMemory.ReadRef<FrameHeader>(readPos);

                // Handle wrap marker
                if (header.IsWrapMarker)
                {
                    _logger.LogDebug("Found wrap marker at position {ReadPos}, handling wrap-around", oieb.PayloadReadPos);

                    // Calculate wasted space from current read position to end of buffer
                    ulong wastedSpace = oieb.PayloadSize - oieb.PayloadReadPos;
                    _logger.LogDebug("Wrap-around: wasted space = {WastedSpace} bytes (from {ReadPos} to {PayloadSize})",
                        wastedSpace, oieb.PayloadReadPos, oieb.PayloadSize);

                    // Update OIEB directly via ref
                    oieb.PayloadFreeBytes += wastedSpace;
                    oieb.PayloadReadPos = 0;
                    oieb.PayloadReadCount++;

                    _logger.LogDebug("After wrap: ReadPos=0, ReadCount={ReadCount}, FreeBytes={FreeBytes}",
                        oieb.PayloadReadCount, oieb.PayloadFreeBytes);

                    _sharedMemory.Flush();

                    // Don't signal semaphore for wrap marker - it's not a logical frame
                    // Re-read header at new position
                    readPos = _payloadOffset;
                    header = ref _sharedMemory.ReadRef<FrameHeader>(readPos);
                }

                if (header.PayloadSize == 0 || header.PayloadSize > oieb.PayloadSize)
                {
                    _logger.LogError("Invalid frame size: {FrameSize} (buffer size: {PayloadSize})", header.PayloadSize, oieb.PayloadSize);
                    throw new InvalidOperationException($"Invalid frame size: {header.PayloadSize}");
                }

                _logger.LogDebug("Reading frame: seq={Sequence}, size={Size} from position {ReadPos}",
                    header.SequenceNumber, header.PayloadSize, oieb.PayloadReadPos);

                // Get pointer to frame data for zero-copy access
                long dataPos = readPos + FrameHeader.SIZE;
                unsafe
                {
                    byte* framePtr = _sharedMemory.GetPointer(dataPos);

                    // Update OIEB directly via ref
                    long nextPos = dataPos + (long)header.PayloadSize - _payloadOffset;
                    ulong newReadPos = (ulong)(nextPos % (long)oieb.PayloadSize);
                    _logger.LogTrace("Updating read position: {OldPos} -> {NewPos} (nextPos={NextPos})",
                        oieb.PayloadReadPos, newReadPos, nextPos);

                    oieb.PayloadReadPos = newReadPos;
                    oieb.PayloadReadCount++;

                    // NOTE: We do NOT update PayloadFreeBytes here!
                    // This will be done when the Frame is disposed (RAII pattern)
                    // This ensures the writer cannot overwrite data still being used

                    _logger.LogDebug("Frame read: seq={Sequence}, new state: ReadCount={ReadCount}, ReadPos={ReadPos}",
                        header.SequenceNumber, oieb.PayloadReadCount, oieb.PayloadReadPos);

                    _sharedMemory.Flush();

                    // Capture values needed for disposal callback
                    var sequenceNumber = header.SequenceNumber;
                    var payloadSize = (int)header.PayloadSize;
                    ulong totalFrameSize = (ulong)FrameHeader.SIZE + header.PayloadSize;

                    // Create Frame with cached disposal callback - zero allocations!
                    // This implements proper RAII - resources are released only when Frame is disposed
                    var frame = new Frame(
                        framePtr,
                        payloadSize,
                        sequenceNumber,
                        OnFrameDisposed);  // Use cached callback

                    return frame;
                }

            }
        }

        /// <summary>
        /// Check if a writer is connected
        /// </summary>
        public bool IsWriterConnected()
        {
            if (_disposed)
                return false;

            // Use ReadRef to avoid copying OIEB
            ref readonly var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
            return oieb.WriterPid != 0 && ProcessExists(oieb.WriterPid);
        }

        /// <summary>
        /// Wait for a writer to connect with timeout
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>True if writer connected within timeout, false otherwise</returns>
        public bool IsWriterConnected(int timeoutMs)
        {
            if (_disposed)
                return false;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (stopwatch.Elapsed < timeout)
            {
                if (IsWriterConnected())
                    return true;

                // Sleep for a short time before checking again
                Thread.Sleep(100);
            }

            return false;
        }

        public string Name => _name;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long CalculateUsedBytes(ulong writePos, ulong readPos, ulong bufferSize)
        {
            return writePos >= readPos
                ? (long)(writePos - readPos)
                : (long)(bufferSize - readPos + writePos);
        }

        private static int AlignToBlockBoundary(int size)
        {
            return (size + Constants.BlockAlignment - 1) / Constants.BlockAlignment * Constants.BlockAlignment;
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

        /// <summary>
        /// Cached callback for frame disposal - called when a Frame is disposed.
        /// This is allocated once and reused for all frames to avoid per-frame allocations.
        /// </summary>
        private void OnFrameDisposed(in Frame frame)
        {
            // Calculate total frame size from the frame itself
            var totalFrameSize = (ulong)(frame.Size + FrameHeader.SIZE);
            
            _logger.LogTrace("Frame disposed, releasing {Size} bytes for seq={Sequence}", 
                totalFrameSize, frame.Sequence);
            
            // Update OIEB to mark space as available (matching C++ pattern)
            ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
            oieb.PayloadFreeBytes += totalFrameSize;
            _sharedMemory.Flush();
            
            // Signal writer that space is available
            _readSemaphore.Release();
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Clear reader PID
            try
            {
                if (_sharedMemory != null!)
                {
                    ref var oieb = ref _sharedMemory.ReadRef<OIEB>(0);
                    oieb.ReaderPid = 0;
                    _sharedMemory.Flush();
                }
            }
            catch
            {
                // Ignore errors - shared memory might be corrupted
            }

            // Dispose our handles (but don't remove the underlying resources)
            _sharedMemory?.Dispose();
            _writeSemaphore?.Dispose();
            _readSemaphore?.Dispose();
            _lock?.Dispose();

            // Note: We don't remove the shared memory and semaphores here.
            // This allows a writer to connect and detect that the reader died.
            // The resources will be cleaned up when:
            // 1. A new reader tries to create the same buffer and detects stale resources
            // 2. The system is restarted
            // 3. Manual cleanup is performed
        }

        /// <summary>
        /// Get the current OIEB state (for testing purposes)
        /// </summary>
        internal unsafe OIEB GetOIEB()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Reader));

            return _sharedMemory.ReadRef<OIEB>(0);
        }
    }
}