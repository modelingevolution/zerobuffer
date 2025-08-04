using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Wrapper for duplex channel responses that handles sequence extraction
    /// </summary>
    public readonly ref struct DuplexResponse
    {
        private readonly Frame _frame;
        
        public DuplexResponse(Frame frame)
        {
            _frame = frame;
        }
        
        /// <summary>
        /// Check if the response is valid
        /// </summary>
        public bool IsValid => _frame.IsValid && _frame.Size >= 8;
        
        /// <summary>
        /// Get the request sequence number from the response
        /// </summary>
        public ulong Sequence
        {
            get
            {
                if (!IsValid)
                    return 0;
                    
                var span = _frame.Span;
                return (ulong)BitConverter.ToInt64(span.Slice(0, 8));
            }
        }
        
        /// <summary>
        /// Get the response data (without sequence prefix)
        /// </summary>
        public byte[] ToArray()
        {
            if (!IsValid)
                return Array.Empty<byte>();
                
            var span = _frame.Span;
            if (span.Length <= 8)
                return Array.Empty<byte>();
                
            return span.Slice(8).ToArray();
        }
        
        /// <summary>
        /// Get the response data as a span (without sequence prefix)
        /// </summary>
        public ReadOnlySpan<byte> Span
        {
            get
            {
                if (!IsValid)
                    return ReadOnlySpan<byte>.Empty;
                    
                var span = _frame.Span;
                if (span.Length <= 8)
                    return ReadOnlySpan<byte>.Empty;
                    
                return span.Slice(8);
            }
        }
    }
}