using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Client-side interface for duplex communication with independent send/receive operations.
    /// Allows for concurrent sending and receiving on separate threads.
    /// </summary>
    public interface IDuplexClient : IDisposable
    {
        /// <summary>
        /// Send a request and return the sequence number for correlation.
        /// This method returns immediately after writing to the request buffer.
        /// </summary>
        /// <param name="data">Request data to send</param>
        /// <returns>Sequence number for correlating the response</returns>
        ulong SendRequest(byte[] data);
        
        /// <summary>
        /// Acquire buffer for zero-copy write. Returns sequence number.
        /// The buffer parameter will be set to a span pointing to the acquired buffer.
        /// Call CommitRequest() after writing to send the request.
        /// </summary>
        /// <param name="size">Size of the buffer to acquire</param>
        /// <param name="buffer">Output span pointing to the acquired buffer</param>
        /// <returns>Sequence number for correlating the response</returns>
        ulong AcquireRequestBuffer(int size, out Span<byte> buffer);
        
        /// <summary>
        /// Commit the request after writing to the acquired buffer
        /// </summary>
        void CommitRequest();
        
        /// <summary>
        /// Receive a response. This method blocks until a response is available or timeout.
        /// The response includes the sequence number for correlation.
        /// </summary>
        /// <param name="timeout">Timeout for receive operation</param>
        /// <returns>Response with sequence number, or invalid response if timeout</returns>
        DuplexResponse ReceiveResponse(TimeSpan timeout);
        
        /// <summary>
        /// Check if server is connected to the request buffer
        /// </summary>
        bool IsServerConnected { get; }
    }
}