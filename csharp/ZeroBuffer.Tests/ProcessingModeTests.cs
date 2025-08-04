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
                    immutableServer.Start(_ => ReadOnlySpan<byte>.Empty, ProcessingMode.ThreadPool));
                Assert.Contains("ThreadPool", ex.Message);
                Assert.Contains("not yet implemented", ex.Message);
            }
            
            // Test MutableDuplexServer
            using (var mutableServer = factory.CreateMutableServer("test-channel-2", config))
            {
                var ex = Assert.Throws<NotSupportedException>(() => 
                    mutableServer.Start(_ => { }, ProcessingMode.ThreadPool));
                Assert.Contains("ThreadPool", ex.Message);
                Assert.Contains("not yet implemented", ex.Message);
            }
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
                immutableServer.Start(_ => ReadOnlySpan<byte>.Empty, ProcessingMode.SingleThread);
                Assert.True(immutableServer.IsRunning);
                immutableServer.Stop();
            }
            
            // Test MutableDuplexServer with explicit SingleThread mode
            using (var mutableServer = factory.CreateMutableServer("test-channel-4", config))
            {
                // Should not throw
                mutableServer.Start(_ => { }, ProcessingMode.SingleThread);
                Assert.True(mutableServer.IsRunning);
                mutableServer.Stop();
            }
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
                immutableServer.Start(_ => ReadOnlySpan<byte>.Empty);
                Assert.True(immutableServer.IsRunning);
                immutableServer.Stop();
            }
            
            // Test MutableDuplexServer with default mode
            using (var mutableServer = factory.CreateMutableServer("test-channel-6", config))
            {
                // Should not throw - uses default SingleThread mode
                mutableServer.Start(_ => { });
                Assert.True(mutableServer.IsRunning);
                mutableServer.Stop();
            }
        }
    }
}