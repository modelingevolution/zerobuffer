namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Factory interface for creating duplex channel clients and servers
    /// </summary>
    public interface IDuplexChannelFactory
    {
        /// <summary>
        /// Create an immutable server (processes immutable requests, returns new response data)
        /// </summary>
        /// <param name="channelName">Name of the duplex channel</param>
        /// <param name="config">Buffer configuration</param>
        /// <returns>Immutable duplex server</returns>
        IImmutableDuplexServer CreateImmutableServer(string channelName, BufferConfig config);
        
        // MutableDuplexServer will be implemented in v2.0.0
        // /// <summary>
        // /// Create a mutable server (mutates request data in-place)
        // /// </summary>
        // /// <param name="channelName">Name of the duplex channel</param>
        // /// <param name="config">Buffer configuration</param>
        // /// <returns>Mutable duplex server</returns>
        // IMutableDuplexServer CreateMutableServer(string channelName, BufferConfig config);
        
        /// <summary>
        /// Connect to existing duplex channel (client-side)
        /// </summary>
        /// <param name="channelName">Name of the duplex channel</param>
        /// <returns>Duplex client</returns>
        IDuplexClient CreateClient(string channelName);
    }
}