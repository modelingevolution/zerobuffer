using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Factory for creating duplex channel clients and servers
    /// </summary>
    public class DuplexChannelFactory : IDuplexChannelFactory
    {
        private static readonly Lazy<DuplexChannelFactory> _instance = 
            new(() => new DuplexChannelFactory());
        
        /// <summary>
        /// Get the singleton instance of the factory
        /// </summary>
        public static IDuplexChannelFactory Instance => _instance.Value;
        
        private DuplexChannelFactory()
        {
        }
        
        /// <inheritdoc/>
        public IImmutableDuplexServer CreateImmutableServer(string channelName, BufferConfig config)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            return new ImmutableDuplexServer(channelName, config);
        }
        
        /// <inheritdoc/>
        public IMutableDuplexServer CreateMutableServer(string channelName, BufferConfig config)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            return new MutableDuplexServer(channelName, config);
        }
        
        /// <inheritdoc/>
        public IDuplexClient CreateClient(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            return new DuplexClient(channelName);
        }
    }
}