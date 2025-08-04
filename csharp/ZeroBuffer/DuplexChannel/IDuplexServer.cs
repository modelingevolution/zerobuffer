using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Handler delegate that returns response data as ReadOnlySpan
    /// </summary>
    /// <param name="request">The request frame</param>
    /// <returns>Response data as ReadOnlySpan (must not be stack-allocated)</returns>
    public delegate ReadOnlySpan<byte> RequestHandler(Frame request);
    
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
        /// Start processing requests with a handler that returns response data as ReadOnlySpan
        /// </summary>
        /// <param name="handler">Handler that processes request and returns response data</param>
        /// <param name="mode">Processing mode (SingleThread or ThreadPool)</param>
        void Start(RequestHandler handler, ProcessingMode mode = ProcessingMode.SingleThread);
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
        /// <param name="mode">Processing mode (SingleThread or ThreadPool)</param>
        void Start(Action<Frame> handler, ProcessingMode mode = ProcessingMode.SingleThread);
    }
}