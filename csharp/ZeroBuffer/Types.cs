using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// Callback delegate for frame disposal with readonly reference
    /// </summary>
    /// <param name="frame">The frame being disposed, passed by readonly reference</param>
    public delegate void FrameCallback(in Frame frame);
    
    /// <summary>
    /// Block alignment requirement for all blocks
    /// </summary>
    public static class Constants
    {
        public const int BlockAlignment = 64;
    }

    /// <summary>
    /// Protocol version structure (4 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
    public struct ProtocolVersion
    {
        public byte Major;     // Major version (breaking changes)
        public byte Minor;     // Minor version (new features, backward compatible)
        public byte Patch;     // Patch version (bug fixes)
        public byte Reserved;  // Reserved (must be 0)
        
        public ProtocolVersion(byte major, byte minor = 0, byte patch = 0)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Reserved = 0;
        }
        
        public bool IsCompatibleWith(ProtocolVersion other)
        {
            return Major == other.Major;  // Same major version required
        }
    }
    
    /// <summary>
    /// Operation Info Exchange Block structure
    /// Must match the C++ OIEB structure exactly for cross-language compatibility
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 128)]
    public struct OIEB
    {
        public uint OiebSize;               // Total OIEB size (always 128 for v1.x.x)
        public ProtocolVersion Version;     // Protocol version (currently 1.0.0)
        
        public ulong MetadataSize;         // Total metadata block size
        public ulong MetadataFreeBytes;    // Free bytes in metadata block
        public ulong MetadataWrittenBytes; // Written bytes in metadata block
        
        public ulong PayloadSize;          // Total payload block size
        public ulong PayloadFreeBytes;     // Free bytes in payload block
        public ulong PayloadWritePos;      // Current write position in buffer
        public ulong PayloadReadPos;       // Current read position in buffer
        public ulong PayloadWrittenCount;  // Number of frames written
        public ulong PayloadReadCount;     // Number of frames read
        
        public ulong WriterPid;            // Writer process ID (0 if none)
        public ulong ReaderPid;            // Reader process ID (0 if none)
        
        // Padding to ensure 128-byte size (4 x 8 bytes = 32 bytes)
        private ulong _reserved1;
        private ulong _reserved2;
        private ulong _reserved3;
        private ulong _reserved4;
        
        /// <summary>
        /// Calculate used bytes in the buffer (optimized with ref readonly)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ulong CalculateUsedBytes()
        {
            return PayloadWritePos >= PayloadReadPos 
                ? PayloadWritePos - PayloadReadPos 
                : PayloadSize - PayloadReadPos + PayloadWritePos;
        }
        
        /// <summary>
        /// Check if there's enough space for a frame (optimized)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasSpaceFor(ulong frameSize)
        {
            return PayloadFreeBytes >= frameSize;
        }
        public static readonly int SIZE = Marshal.SizeOf<OIEB>();
    }

    /// <summary>
    /// Frame header structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
    public struct FrameHeader
    {
        public ulong PayloadSize;      // Size of the frame data
        public ulong SequenceNumber;   // Sequence number
        
        // According to protocol: wrap marker has payload_size = 0
        public bool IsWrapMarker => PayloadSize == 0;
        public static readonly int SIZE = Marshal.SizeOf<FrameHeader>();
    }

    /// <summary>
    /// Configuration for creating a buffer
    /// </summary>
    public class BufferConfig
    {
        public int MetadataSize { get; set; }
        public int PayloadSize { get; set; }
        
        public BufferConfig(int metadataSize = 1024, int payloadSize = 1024 * 1024)
        {
            MetadataSize = metadataSize;
            PayloadSize = payloadSize;
        }
    }

    /// <summary>
    /// Frame reference for TRUE zero-copy access
    /// Using unsafe pointers for direct memory access without any copying
    /// </summary>
    public readonly unsafe ref struct Frame
    {
        private readonly byte* _dataPtr;
        private readonly int _length;
        private readonly FrameCallback? _onDispose;
        
        public ulong Sequence { get; }
        
        public bool IsValid => _dataPtr != null && _length > 0;
        
        public int Size => _length;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Frame(byte* dataPtr, int length, ulong sequence) : this(dataPtr, length, sequence, null)
        {
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Frame(byte* dataPtr, int length, ulong sequence, FrameCallback? onDispose)
        {
            _dataPtr = dataPtr;
            _length = length;
            Sequence = sequence;
            _onDispose = onDispose;
        }
        
        /// <summary>
        /// Get span for direct zero-copy access to frame data in shared memory
        /// </summary>
        public ReadOnlySpan<byte> Span => _dataPtr != null ? new ReadOnlySpan<byte>(_dataPtr, _length) : ReadOnlySpan<byte>.Empty;
        public byte* Pointer => _dataPtr;
        /// <summary>
        /// Get mutable span for direct zero-copy write access to frame data.
        /// Use with caution - modifying shared memory requires coordination.
        /// </summary>
        public Span<byte> GetMutableSpan() => _dataPtr != null ? new Span<byte>(_dataPtr, _length) : Span<byte>.Empty;
        
        /// <summary>
        /// Copy frame data to a byte array (only use when a copy is truly needed)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] ToArray()
        {
            if (_dataPtr == null || _length == 0)
                return Array.Empty<byte>();
                
            var array = new byte[_length];
            new ReadOnlySpan<byte>(_dataPtr, _length).CopyTo(array);
            return array;
        }
        
        /// <summary>
        /// Get the internal data pointer for creating FrameRef
        /// </summary>
        internal byte* GetDataPointer() => _dataPtr;
        
        /// <summary>
        /// Dispose method for ref struct disposable pattern (C# 8.0+)
        /// This enables 'using' statements without implementing IDisposable
        /// </summary>
        public void Dispose()
        {
            // Call the disposal callback with 'this' passed by readonly reference
            _onDispose?.Invoke(in this);
        }
        
        // Invalid frame sentinel
        public static Frame Invalid => new Frame(null, 0, 0, null);
    }
}