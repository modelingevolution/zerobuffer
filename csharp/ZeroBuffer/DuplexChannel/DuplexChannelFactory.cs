using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;

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

        bool IDuplexChannelFactory.WaitExists(string channelName, TimeSpan t)
        {
            return ImmutableDuplexServer.WaitExists(channelName, t);
        }
        public static bool WaitExists(string channelName, TimeSpan timeout, ILogger? logger = null) => ImmutableDuplexServer.WaitExists(channelName, timeout, logger);

        /// <inheritdoc/>
        public IImmutableDuplexServer CreateImmutableServer(string channelName, BufferConfig config, TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            var logger = _loggerFactory.CreateLogger<ImmutableDuplexServer>();
            return new ImmutableDuplexServer(channelName, config, timeout ?? TimeSpan.FromSeconds(5), logger);
        }
        
        // MutableDuplexServer will be implemented in v2.0.0
        // /// <inheritdoc/>
        // public IMutableDuplexServer CreateMutableServer(string channelName, BufferConfig config)
        // {
        //     throw new NotImplementedException("MutableDuplexServer will be available in v2.0.0");
        // }
        
        /// <inheritdoc/>
        public IDuplexClient CreateClient(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
                
            return new DuplexClient(channelName);
        }
    }
}