using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Tests
{
    /// <summary>
    /// Tests specifically for verifying that free space accounting works correctly
    /// with the increment/decrement approach, especially around wrap-around scenarios
    /// </summary>
    public class FreeSpaceAccountingTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _bufferName;

        public FreeSpaceAccountingTests(ITestOutputHelper output)
        {
            _output = output;
            _bufferName = $"test-freespace-{Guid.NewGuid():N}";
        }

        public void Dispose()
        {
            // Cleanup is handled by Reader/Writer dispose
        }

        [Fact]
        public void FreeSpaceCorrectAfterSimpleWrap()
        {
            _output.WriteLine("Testing free space accounting after simple wrap...");

            // Create a small buffer to force wrapping
            var config = new BufferConfig(100, 1000); // 1KB payload
            
            using (var reader = new Reader(_bufferName, config))
            using (var writer = new Writer(_bufferName))
            {
                // Get initial free space
                var initialFreeSpace = GetFreeSpace(reader);
                _output.WriteLine($"Initial free space: {initialFreeSpace}");
                // The actual size will be aligned to block boundaries (1024 instead of 1000)
                var alignedSize = (ulong)((1000 + Constants.BlockAlignment - 1) / Constants.BlockAlignment * Constants.BlockAlignment);
                Assert.Equal(alignedSize, initialFreeSpace);

                // Write frames until we're near the end
                int frameSize = 100;
                byte[] data = new byte[frameSize];
                
                // Write 8 frames (800 bytes + headers)
                for (int i = 0; i < 8; i++)
                {
                    writer.WriteFrame(data);
                }

                // Read all frames to free up space
                for (int i = 0; i < 8; i++)
                {
                    using var frame = reader.ReadFrame();
                    Assert.Equal(frameSize, frame.Size);
                }

                var afterReadSpace = GetFreeSpace(reader);
                _output.WriteLine($"Free space after reading all frames: {afterReadSpace}");
                Assert.Equal(initialFreeSpace, afterReadSpace);

                // Now write a frame that will cause wrapping
                // Current write position should be around 800+ bytes
                writer.WriteFrame(data); // This should wrap

                // Read the wrapped frame
                {
                    using var wrappedFrame = reader.ReadFrame();
                    Assert.Equal(frameSize, wrappedFrame.Size);
                }

                var finalFreeSpace = GetFreeSpace(reader);
                _output.WriteLine($"Final free space: {finalFreeSpace}");
                Assert.Equal(initialFreeSpace, finalFreeSpace);
            }
        }

        [Fact]
        public void FreeSpaceCorrectAfterMultipleWraps()
        {
            _output.WriteLine("Testing free space accounting after multiple wraps...");

            var config = new BufferConfig(100, 500); // Small buffer to force frequent wraps
            
            using (var reader = new Reader(_bufferName, config))
            using (var writer = new Writer(_bufferName))
            {
                var initialFreeSpace = GetFreeSpace(reader);
                _output.WriteLine($"Initial free space: {initialFreeSpace}");

                // Do 10 wrap cycles
                for (int cycle = 0; cycle < 10; cycle++)
                {
                    _output.WriteLine($"\nCycle {cycle + 1}:");
                    
                    // Write 3 frames that together will cause a wrap
                    byte[] data = new byte[120]; // With header, ~128 bytes each
                    for (int i = 0; i < 3; i++)
                    {
                        writer.WriteFrame(data);
                    }

                    // Read all frames
                    for (int i = 0; i < 3; i++)
                    {
                        using var frame = reader.ReadFrame();
                        Assert.Equal(120, frame.Size);
                    }

                    var currentFreeSpace = GetFreeSpace(reader);
                    _output.WriteLine($"Free space after cycle: {currentFreeSpace}");
                    
                    // Free space should return to initial value
                    Assert.Equal(initialFreeSpace, currentFreeSpace);
                }
            }
        }

        [Fact]
        public void FreeSpaceWithPartialReads()
        {
            _output.WriteLine("Testing free space with partial reads...");

            var config = new BufferConfig(100, 1000);
            
            using (var reader = new Reader(_bufferName, config))
            using (var writer = new Writer(_bufferName))
            {
                var initialFreeSpace = GetFreeSpace(reader);

                // Write 5 frames
                byte[] data = new byte[100];
                for (int i = 0; i < 5; i++)
                {
                    writer.WriteFrame(data);
                }

                var afterWriteFreeSpace = GetFreeSpace(reader);
                ulong expectedUsed = (ulong)(5 * (Marshal.SizeOf<FrameHeader>() + 100));
                Assert.Equal(initialFreeSpace - expectedUsed, afterWriteFreeSpace);

                // Read only 2 frames
                for (int i = 0; i < 2; i++)
                {
                    reader.ReadFrame().Dispose();
                }

                var afterPartialReadSpace = GetFreeSpace(reader);
                ulong expectedFreed = (ulong)(2 * (Marshal.SizeOf<FrameHeader>() + 100));
                Assert.Equal(afterWriteFreeSpace + expectedFreed, afterPartialReadSpace);

                // Read remaining 3 frames
                for (int i = 0; i < 3; i++)
                {
                    reader.ReadFrame().Dispose();
                }

                var finalFreeSpace = GetFreeSpace(reader);
                Assert.Equal(initialFreeSpace, finalFreeSpace);
            }
        }

        [Fact]
        public void WrapMarkerSpaceAccountedCorrectly()
        {
            _output.WriteLine("Testing wrap marker space accounting...");

            // Create buffer where we know exactly where wrap will occur
            var config = new BufferConfig(100, 1000);
            
            using (var reader = new Reader(_bufferName, config))
            using (var writer = new Writer(_bufferName))
            {
                // Fill buffer to specific position
                int frameSize = 100;
                byte[] data = new byte[frameSize];
                
                // Write 9 frames (900 bytes of data + headers)
                // This should position us where next frame won't fit
                for (int i = 0; i < 9; i++)
                {
                    writer.WriteFrame(data);
                    reader.ReadFrame().Dispose(); // Read immediately to keep free space available
                }

                var beforeWrapSpace = GetFreeSpace(reader);
                _output.WriteLine($"Free space before wrap: {beforeWrapSpace}");

                // Write one more frame that should cause wrap
                writer.WriteFrame(data);
                
                // At this point, wrap marker was written and space at end was wasted
                var afterWrapWriteSpace = GetFreeSpace(reader);
                _output.WriteLine($"Free space after wrap write: {afterWrapWriteSpace}");

                // Read the frame (which includes processing the wrap marker)
                {
                    using var frame = reader.ReadFrame();
                    Assert.Equal(frameSize, frame.Size);
                }
                var afterWrapReadSpace = GetFreeSpace(reader);
                _output.WriteLine($"Free space after wrap read: {afterWrapReadSpace}");
                
                // Free space should be back to original
                Assert.Equal(beforeWrapSpace, afterWrapReadSpace);
            }
        }

        [Fact]
        public void StressTestFreeSpaceAccounting()
        {
            _output.WriteLine("Stress testing free space accounting...");

            var config = new BufferConfig(100, 10000); // 10KB buffer
            
            using (var reader = new Reader(_bufferName, config))
            using (var writer = new Writer(_bufferName))
            {
                var initialFreeSpace = GetFreeSpace(reader);
                var random = new Random(42);
                int totalWritten = 0;
                int totalRead = 0;

                // Do many random operations
                for (int i = 0; i < 1000; i++)
                {
                    // Randomly decide to write or read
                    if (random.Next(2) == 0 && totalWritten - totalRead < 50)
                    {
                        // Write a random sized frame
                        int size = random.Next(10, 200);
                        byte[] data = new byte[size];
                        
                        try
                        {
                            writer.WriteFrame(data);
                            totalWritten++;
                        }
                        catch (BufferFullException)
                        {
                            // Expected when buffer is full
                        }
                    }
                    else if (totalRead < totalWritten)
                    {
                        // Read a frame
                        using var frame = reader.ReadFrame();
                        if (frame.IsValid)
                        {
                            totalRead++;
                        }
                    }
                }

                // Read all remaining frames
                while (totalRead < totalWritten)
                {
                    using var frame = reader.ReadFrame();
                    if (frame.IsValid)
                    {
                        totalRead++;
                    }
                }

                var finalFreeSpace = GetFreeSpace(reader);
                _output.WriteLine($"Initial free space: {initialFreeSpace}");
                _output.WriteLine($"Final free space: {finalFreeSpace}");
                _output.WriteLine($"Total frames written: {totalWritten}");
                _output.WriteLine($"Total frames read: {totalRead}");
                
                // All space should be freed
                Assert.Equal(initialFreeSpace, finalFreeSpace);
            }
        }

        private unsafe ulong GetFreeSpace(Reader reader)
        {
            var oieb = (OIEB)reader.GetOIEB();


            return oieb.PayloadFreeBytes;
        }
    }
}