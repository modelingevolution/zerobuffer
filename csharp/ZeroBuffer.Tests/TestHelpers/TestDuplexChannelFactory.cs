using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Xunit.Extensions.Logging;
using ZeroBuffer.DuplexChannel;

namespace ZeroBuffer.Tests.TestHelpers
{
    /// <summary>
    /// Test helper for creating duplex channel factories with xUnit logging
    /// </summary>
    internal static class TestDuplexChannelFactory
    {
        /// <summary>
        /// Creates a factory instance with xUnit test output logging
        /// </summary>
        public static IDuplexChannelFactory Create(ITestOutputHelper output)
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddXunit(output);
            return new DuplexChannelFactory(loggerFactory);
        }
        
        /// <summary>
        /// Creates a factory instance without logging
        /// </summary>
        public static IDuplexChannelFactory Create()
        {
            return new DuplexChannelFactory();
        }
    }
}