using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Handler delegate that processes a request and writes response directly to the Writer
    /// </summary>
    /// <param name="request">The request frame</param>
    /// <param name="responseWriter">The writer to write response data to</param>
    public delegate void RequestHandler(Frame request, Writer responseWriter);
    
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
        /// Start processing requests with a handler that writes response directly to Writer
        /// </summary>
        /// <param name="onFrame">Handler that processes request and writes response to Writer</param>
        /// <param name="onInit">Called once, before the first invocation of onFrame</param>
        /// <param name="mode">Processing mode (SingleThread or ThreadPool)</param>
        void Start(RequestHandler onFrame, Action<ReadOnlySpan<byte>> onInit = null, ProcessingMode mode = ProcessingMode.SingleThread);
    }
    
    // MutableDuplexServer will be implemented in v2.0.0
    // It will support in-place modification with shared payload buffers
}