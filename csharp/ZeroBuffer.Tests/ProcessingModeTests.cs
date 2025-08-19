using System;
using Xunit;
using ZeroBuffer.DuplexChannel;

namespace ZeroBuffer.Tests
{
    public class ProcessingModeTests
    {
        [Fact]
        public void ThreadPoolMode_ThrowsNotSupportedException()
        {
            var factory = new DuplexChannelFactory();
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Test ImmutableDuplexServer
            using (var immutableServer = factory.CreateImmutableServer("test-channel-1", config))
            {
                var ex = Assert.Throws<NotSupportedException>(() => 
                    immutableServer.Start((_, __) => { }, ProcessingMode.ThreadPool));
                Assert.Contains("ThreadPool", ex.Message);
                Assert.Contains("not yet implemented", ex.Message);
            }
            
            // v1.0.0: MutableDuplexServer is not supported
            // Will be implemented in v2.0.0
        }
        
        [Fact]
        public void SingleThreadMode_WorksAsExpected()
        {
            var factory = new DuplexChannelFactory();
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Test ImmutableDuplexServer with explicit SingleThread mode
            using (var immutableServer = factory.CreateImmutableServer("test-channel-3", config))
            {
                // Should not throw
                immutableServer.Start((_, __) => { }, ProcessingMode.SingleThread);
                Assert.True(immutableServer.IsRunning);
                immutableServer.Stop();
            }
            
            // v1.0.0: MutableDuplexServer is not supported
            // Will be implemented in v2.0.0
        }
        
        [Fact]
        public void DefaultMode_IsSingleThread()
        {
            var factory = new DuplexChannelFactory();
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Test ImmutableDuplexServer with default mode
            using (var immutableServer = factory.CreateImmutableServer("test-channel-5", config))
            {
                // Should not throw - uses default SingleThread mode
                immutableServer.Start((_, __) => { });
                Assert.True(immutableServer.IsRunning);
                immutableServer.Stop();
            }
            
            // v1.0.0: MutableDuplexServer is not supported
            // Will be implemented in v2.0.0
        }
        
        [Fact]
        public void MutableServer_NotSupportedInV1()
        {
            var factory = new DuplexChannelFactory();
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // v1.0.0: CreateMutableServer should not exist in the API
            // This test documents that MutableDuplexServer is planned for v2.0.0
            
            // Verify the factory only creates immutable servers
            var server = factory.CreateImmutableServer("test-channel-v1", config);
            Assert.NotNull(server);
            Assert.IsType<ImmutableDuplexServer>(server);
            server.Dispose();
        }
    }
}