using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Tests
{
    public partial class ResourceCleanupTests
    {
        [Fact]
        public void CleanupStaleResourcesWithRandomNames()
        {
            _output.WriteLine("Testing cleanup of abandoned buffers with random names...");
            
            // Create multiple buffers with random names
            var randomNames = new string[5];
            for (int i = 0; i < randomNames.Length; i++)
            {
                randomNames[i] = $"test-random-{Guid.NewGuid():N}";
            }
            
            // Create and abandon buffers
            _output.WriteLine("Creating and abandoning buffers with random names...");
            foreach (var name in randomNames)
            {
                using (var reader = new Reader(name, new BufferConfig(1024, 10240)))
                using (var writer = new Writer(name))
                {
                    writer.WriteFrame(new byte[] { 0x42 });
                    var frame = reader.ReadFrame();
                    Assert.Equal(1, frame.Size);
                }
                // Resources might or might not exist immediately after dispose
                // We don't check here - the important part is they get cleaned up later
            }
            
            // Add a small delay to ensure resources are properly released
            Thread.Sleep(100);
            
            // Now create a new buffer - this should trigger cleanup of all stale resources
            _output.WriteLine("Creating new buffer to trigger cleanup...");
            var triggerName = CreateTestBufferName("cleanup-trigger");
            using (var reader = new Reader(triggerName, new BufferConfig(1024, 10240)))
            {
                // The constructor should have cleaned up all stale resources
                
                // Verify all random-named buffers were cleaned up
                _output.WriteLine("Verifying random-named buffers were cleaned up...");
                foreach (var name in randomNames)
                {
                    Assert.False(SharedMemoryExists(name), $"Buffer {name} should have been cleaned up");
                }
            }
            
            _output.WriteLine("✓ Successfully cleaned up abandoned buffers with random names");
        }
        
        [Fact]
        public void OnlyCleanupAbandonedBuffers()
        {
            _output.WriteLine("Testing that active buffers are not cleaned up...");
            
            // Create an abandoned buffer
            var abandonedName = CreateTestBufferName("cleanup-abandoned");
            using (var reader = new Reader(abandonedName, new BufferConfig(1024, 10240)))
            using (var writer = new Writer(abandonedName))
            {
                writer.WriteFrame(new byte[] { 0x11 });
                reader.ReadFrame();
            }
            
            // Create an active buffer (keep reader alive)
            var activeName = CreateTestBufferName("cleanup-active");
            using (var activeReader = new Reader(activeName, new BufferConfig(1024, 10240)))
            {
                using (var activeWriter = new Writer(activeName))
                {
                    activeWriter.WriteFrame(new byte[] { 0x22 });
                }
                
                // Small delay
                Thread.Sleep(100);
                
                // Create another buffer to trigger cleanup
                var triggerName = CreateTestBufferName("cleanup-trigger2");
                using (var triggerReader = new Reader(triggerName, new BufferConfig(1024, 10240)))
                {
                    // Abandoned buffer should be cleaned up
                    Assert.False(SharedMemoryExists(abandonedName), "Abandoned buffer should be cleaned up");
                    
                    // Active buffer should still exist
                    Assert.True(SharedMemoryExists(activeName), "Active buffer should not be cleaned up");
                }
            }
            
            _output.WriteLine("✓ Only abandoned buffers were cleaned up");
        }
    }
}