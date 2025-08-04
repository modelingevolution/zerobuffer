using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Base server-side interface with common functionality.
    /// </summary>
    public interface IDuplexServer : IDisposable
    {
        /// <summary>
        /// Stop processing
        /// </summary>
        void Stop();
        
        /// <summary>
        /// Check if running
        /// </summary>
        bool IsRunning { get; }
    }
    
    /// <summary>
    /// Server that processes immutable requests and returns new response data
    /// </summary>
    public interface IImmutableDuplexServer : IDuplexServer
    {
        /// <summary>
        /// Start processing requests with a handler that returns new data
        /// </summary>
        /// <param name="handler">Function that processes request and returns response data</param>
        void Start(Func<Frame, byte[]> handler);
    }
    
    /// <summary>
    /// Server that mutates request data in-place (zero-copy)
    /// </summary>
    public interface IMutableDuplexServer : IDuplexServer
    {
        /// <summary>
        /// Start processing with mutable handler
        /// </summary>
        /// <param name="handler">Action that modifies frame data in-place</param>
        void Start(Action<Frame> handler);
    }
}