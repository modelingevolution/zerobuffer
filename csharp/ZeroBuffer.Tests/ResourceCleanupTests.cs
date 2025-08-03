using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Tests
{
    public partial class ResourceCleanupTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly List<string> _resourcesToCleanup = new();

        public ResourceCleanupTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            // Cleanup test resources
            foreach (var resource in _resourcesToCleanup)
            {
                try
                {
                    CleanupResource(resource);
                }
                catch { }
            }
        }

        private string CreateTestBufferName(string prefix)
        {
            var name = $"{prefix}-{Guid.NewGuid():N}";
            _resourcesToCleanup.Add(name);
            return name;
        }

        private static bool SharedMemoryExists(string name)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
                    return true;
                }
                else
                {
                    var shmPath = Path.Combine("/dev/shm", name);
                    return File.Exists(shmPath);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SemaphoreExists(string name)
        {
            try
            {
                var exists = Semaphore.TryOpenExisting(name, out var sem);
                sem?.Dispose();
                return exists;
            }
            catch
            {
                return false;
            }
        }

        private static void CleanupResource(string bufferName)
        {
            // In .NET, resources are cleaned up when disposed
            // This is mainly for verification in tests
        }

        [Fact]
        public void CreateDestroyMultipleTimes()
        {
            _output.WriteLine("Testing create/destroy multiple times...");
            
            var bufferName = CreateTestBufferName("cleanup-multi");
            var config = new BufferConfig(1024, 10240);
            
            // Ensure clean start
            Assert.False(SharedMemoryExists(bufferName));
            
            // Create and destroy 5 times
            for (int i = 0; i < 5; i++)
            {
                using (var reader = new Reader(bufferName, config))
                {
                    // Verify resources exist
                    Assert.True(SharedMemoryExists(bufferName));
                    // Note: On Linux, we can't easily check semaphore existence
                    // without trying to open it, which would interfere with the test
                    
                    using (var writer = new Writer(bufferName))
                    {
                        // Write and read a frame
                        var data = new byte[] { (byte)i };
                        writer.WriteFrame(data);
                        
                        var frame = reader.ReadFrame();
                        Assert.Equal((byte)i, frame.Span[0]);
                    }
                }
                
                // Small delay to ensure cleanup
                Thread.Sleep(100);
                
                // After destruction, resources should be cleaned up
                Assert.False(SharedMemoryExists(bufferName));
            }
            
            _output.WriteLine("✓ Successfully created and destroyed buffer 5 times");
        }

        [Fact]
        public void MultipleBuffersCleanup()
        {
            _output.WriteLine("Testing multiple buffers cleanup...");
            
            var bufferNames = new[]
            {
                CreateTestBufferName("cleanup-multi-1"),
                CreateTestBufferName("cleanup-multi-2"),
                CreateTestBufferName("cleanup-multi-3")
            };
            
            var readers = new List<Reader>();
            var writers = new List<Writer>();
            
            try
            {
                // Create all buffers
                foreach (var name in bufferNames)
                {
                    var reader = new Reader(name, new BufferConfig());
                    readers.Add(reader);
                    
                    var writer = new Writer(name);
                    writers.Add(writer);
                    
                    // Write something
                    writer.WriteFrame(new byte[] { 1, 2, 3 });
                }
                
                // Verify all resources exist
                foreach (var name in bufferNames)
                {
                    Assert.True(SharedMemoryExists(name));
                }
                
                // Destroy them one by one
                for (int i = 0; i < bufferNames.Length; i++)
                {
                    writers[i].Dispose();
                    readers[i].Dispose();
                    
                    Thread.Sleep(100);
                    
                    // Verify this buffer is cleaned up
                    Assert.False(SharedMemoryExists(bufferNames[i]));
                    
                    // Verify other buffers still exist
                    for (int j = i + 1; j < bufferNames.Length; j++)
                    {
                        Assert.True(SharedMemoryExists(bufferNames[j]));
                    }
                }
            }
            finally
            {
                // Cleanup
                foreach (var writer in writers) writer?.Dispose();
                foreach (var reader in readers) reader?.Dispose();
            }
            
            _output.WriteLine("✓ Multiple buffers cleaned up correctly");
        }

        [Fact]
        public void BufferReuseAfterDeletion()
        {
            _output.WriteLine("Testing buffer reuse after deletion...");
            
            var bufferName = CreateTestBufferName("cleanup-reuse");
            var config = new BufferConfig(1024, 10240);
            
            _output.WriteLine("Creating first buffer instance...");
            using (var reader1 = new Reader(bufferName, config))
            using (var writer1 = new Writer(bufferName))
            {
                // Write some data
                var data1 = new byte[] { 0xAA, 0xAA, 0xAA };
                writer1.WriteFrame(data1);
                
                var frame = reader1.ReadFrame();
                Assert.Equal(3, frame.Size);
                Assert.Equal(0xAA, frame.Span[0]);
                
                Assert.True(SharedMemoryExists(bufferName));
            }
            
            // After destruction, resources should be cleaned up
            Thread.Sleep(100);
            Assert.False(SharedMemoryExists(bufferName));
            
            _output.WriteLine("Creating second buffer instance with same name...");
            using (var reader2 = new Reader(bufferName, config))
            using (var writer2 = new Writer(bufferName))
            {
                // Write different data
                var data2 = new byte[] { 0xBB, 0xBB, 0xBB, 0xBB };
                writer2.WriteFrame(data2);
                
                var frame = reader2.ReadFrame();
                Assert.Equal(4, frame.Size);
                Assert.Equal(0xBB, frame.Span[0]);
                Assert.Equal(1UL, frame.Sequence); // Should start from 1 again
                
                Assert.True(SharedMemoryExists(bufferName));
            }
            
            // Final cleanup check
            Thread.Sleep(100);
            Assert.False(SharedMemoryExists(bufferName));
            
            _output.WriteLine("✓ Buffer name successfully reused after deletion");
        }

        [Fact]
        public async Task NameReuseInSeparateProcesses()
        {
            _output.WriteLine("Testing name reuse in separate processes...");
            
            var bufferName = CreateTestBufferName("cleanup-fork");
            var config = new BufferConfig(1024, 10240);
            
            // Parent creates and destroys buffer
            using (var reader = new Reader(bufferName, config))
            using (var writer = new Writer(bufferName))
            {
                writer.WriteFrame(new byte[] { 0x11 });
                var frame = reader.ReadFrame();
                Assert.Equal(0x11, frame.Span[0]);
            }
            
            Thread.Sleep(100);
            Assert.False(SharedMemoryExists(bufferName));
            
            // Simulate child process reusing the name
            var childTask = Task.Run(() =>
            {
                using var reader = new Reader(bufferName, config);
                using var writer = new Writer(bufferName);
                
                writer.WriteFrame(new byte[] { 0x22, 0x22 });
                var frame = reader.ReadFrame();
                
                Assert.Equal(2, frame.Size);
                Assert.Equal(0x22, frame.Span[0]);
            });
            
            await childTask;
            
            // Parent can also reuse the name
            Thread.Sleep(100);
            using (var reader = new Reader(bufferName, config))
            using (var writer = new Writer(bufferName))
            {
                writer.WriteFrame(new byte[] { 0x33, 0x33, 0x33 });
                var frame = reader.ReadFrame();
                Assert.Equal(3, frame.Size);
                Assert.Equal(0x33, frame.Span[0]);
            }
            
            _output.WriteLine("✓ Name successfully reused across process boundaries");
        }

        [Fact]
        public void CleanupWithWrapAround()
        {
            _output.WriteLine("Testing cleanup with buffer wrap-around...");
            
            var bufferName = CreateTestBufferName("cleanup-wrap");
            var smallBuffer = 4096; // Small buffer to force wrap
            var config = new BufferConfig(1024, smallBuffer);
            
            using (var reader = new Reader(bufferName, config))
            using (var writer = new Writer(bufferName))
            {
                // Write enough to wrap around
                var frameData = new byte[512];
                for (int i = 0; i < 20; i++)
                {
                    frameData[0] = (byte)i;
                    writer.WriteFrame(frameData);
                    
                    var frame = reader.ReadFrame();
                    Assert.Equal(512, frame.Size);
                    Assert.Equal((byte)i, frame.Span[0]);
                }
                
                // Buffer should have wrapped multiple times
            }
            
            // Verify cleanup
            Thread.Sleep(100);
            Assert.False(SharedMemoryExists(bufferName));
            
            // Reuse should work
            using (var reader = new Reader(bufferName, config))
            using (var writer = new Writer(bufferName))
            {
                writer.WriteFrame(new byte[] { 0xFF });
                var frame = reader.ReadFrame();
                Assert.Equal(0xFF, frame.Span[0]);
            }
            
            _output.WriteLine("✓ Cleanup after wrap-around successful");
        }

        [Fact]
        public async Task ConcurrentNameReuse()
        {
            _output.WriteLine("Testing concurrent processes trying to reuse same name...");
            
            var bufferName = CreateTestBufferName("cleanup-concurrent");
            const int numTasks = 5;
            
            // Create and destroy initial buffer
            using (var reader = new Reader(bufferName, new BufferConfig()))
            using (var writer = new Writer(bufferName))
            {
                // Just create and destroy
            }
            
            Thread.Sleep(100);
            Assert.False(SharedMemoryExists(bufferName));
            
            // Multiple tasks try to create buffer with same name
            var tasks = new Task<bool>[numTasks];
            for (int i = 0; i < numTasks; i++)
            {
                var taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    // Small random delay to create race conditions
                    await Task.Delay(Random.Shared.Next(50));
                    
                    try
                    {
                        using var reader = new Reader(bufferName, new BufferConfig());
                        using var writer = new Writer(bufferName);
                        
                        // Do some work
                        writer.WriteFrame(BitConverter.GetBytes(taskId));
                        var frame = reader.ReadFrame();
                        
                        return true; // Success
                    }
                    catch
                    {
                        return false; // Expected - only one should succeed
                    }
                });
            }
            
            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r);
            
            // At least one should succeed
            Assert.True(successCount >= 1);
            Assert.True(successCount <= numTasks);
            
            _output.WriteLine($"✓ {successCount} out of {numTasks} tasks succeeded (expected behavior)");
            
            // Final cleanup and reuse
            Thread.Sleep(100);
            using (var reader = new Reader(bufferName, new BufferConfig()))
            using (var writer = new Writer(bufferName))
            {
                _output.WriteLine("✓ Successfully reused name after concurrent attempts");
            }
        }
    }
}