using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Factory for creating duplex channel clients and servers
    /// </summary>
    public class DuplexChannelFactory : IDuplexChannelFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        
        /// <summary>
        /// Creates a new instance of the duplex channel factory
        /// </summary>
        /// <param name="loggerFactory">Optional logger factory for creating loggers</param>
        public DuplexChannelFactory(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }
        
        /// <inheritdoc/>
        public IImmutableDuplexServer CreateImmutableServer(string channelName, BufferConfig config)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            var logger = _loggerFactory.CreateLogger<ImmutableDuplexServer>();
            return new ImmutableDuplexServer(channelName, config, logger);
        }
        
        /// <inheritdoc/>
        public IMutableDuplexServer CreateMutableServer(string channelName, BufferConfig config)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            var logger = _loggerFactory.CreateLogger<MutableDuplexServer>();
            return new MutableDuplexServer(channelName, config, logger);
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