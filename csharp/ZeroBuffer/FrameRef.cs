using System;
using System.Runtime.InteropServices;

namespace ZeroBuffer
{
    /// <summary>
    /// Non-ref struct that holds Frame data pointers for scenarios where ref structs can't be used (e.g., storing in fields)
    /// This unsafe class holds a direct pointer to the frame data in shared memory
    /// IMPORTANT: The frame data must remain valid for the lifetime of this FrameRef
    /// </summary>
    public unsafe class FrameRef
    {
        private readonly byte* _dataPtr;
        private readonly int _size;
        
        public ulong Sequence { get; }
        public int Size => _size;
        public bool IsValid => _dataPtr != null && _size > 0;
        
        /// <summary>
        /// Get the frame data as a span (zero-copy access)
        /// </summary>
        public ReadOnlySpan<byte> Span => _dataPtr != null ? new ReadOnlySpan<byte>(_dataPtr, _size) : ReadOnlySpan<byte>.Empty;
        
        /// <summary>
        /// Get the frame data as a byte array (copies data)
        /// </summary>
        public byte[] Data => ToArray();
        
        public FrameRef(byte* dataPtr, int size, ulong sequence)
        {
            _dataPtr = dataPtr;
            _size = size;
            Sequence = sequence;
        }
        
        /// <summary>
        /// Create a FrameRef from a Frame's internal pointer
        /// The Frame points to shared memory that remains valid
        /// </summary>
        public static unsafe FrameRef FromFrame(Frame frame)
        {
            if (!frame.IsValid)
            {
                throw new ArgumentException("Cannot create FrameRef from invalid frame");
            }
            
            // Get the direct pointer from Frame - it points to shared memory that remains valid
            return new FrameRef(frame.GetDataPointer(), frame.Size, frame.Sequence);
        }
        
        /// <summary>
        /// Copy frame data to a byte array
        /// </summary>
        public byte[] ToArray()
        {
            if (_dataPtr == null || _size == 0)
                return Array.Empty<byte>();
                
            var array = new byte[_size];
            new ReadOnlySpan<byte>(_dataPtr, _size).CopyTo(array);
            return array;
        }
        
        /// <summary>
        /// Invalid frame reference sentinel
        /// </summary>
        public static FrameRef Invalid => new FrameRef(null, 0, 0);
    }
    
    // Extension method for Frame
    public static class FrameExtensions
    {
        /// <summary>
        /// Convert Frame to FrameRef (holds pointer to shared memory)
        /// </summary>
        public static unsafe FrameRef ToFrameRef(this Frame frame)
        {
            return FrameRef.FromFrame(frame);
        }
    }
}