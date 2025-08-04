namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Processing mode for duplex servers
    /// </summary>
    public enum ProcessingMode
    {
        /// <summary>
        /// Process requests sequentially in one background thread
        /// </summary>
        SingleThread,
        
        /// <summary>
        /// Process each request in a thread pool (not yet implemented)
        /// </summary>
        ThreadPool
    }
}