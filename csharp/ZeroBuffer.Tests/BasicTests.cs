using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ZeroBuffer.Tests
{
    public class BasicTests
    {
        [Fact]
        public void CanCreateAndDestroyBuffer()
        {
            const string bufferName = "test-buffer-create";
            var config = new BufferConfig(1024, 64 * 1024);
            
            using (var reader = new Reader(bufferName, config))
            {
                Assert.Equal(bufferName, reader.Name);
                Assert.True(reader.IsWriterConnected() == false);
            }
        }

        [Fact]
        public void CanWriteAndReadMetadata()
        {
            const string bufferName = "test-buffer-metadata";
            var config = new BufferConfig(1024, 64 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Write metadata
            var metadata = Encoding.UTF8.GetBytes("Test metadata");
            writer.SetMetadata(metadata);
            
            // Read metadata
            var readMetadata = reader.GetMetadata();
            Assert.Equal(metadata.Length, readMetadata.Length);
            Assert.Equal(metadata, readMetadata.ToArray());
        }

        [Fact]
        public void CanWriteAndReadFrames()
        {
            const string bufferName = "test-buffer-frames";
            var config = new BufferConfig(1024, 64 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Write frames
            for (int i = 0; i < 10; i++)
            {
                var data = Encoding.UTF8.GetBytes($"Frame {i}");
                writer.WriteFrame(data);
            }
            
            // Read frames
            for (int i = 0; i < 10; i++)
            {
                var frame = reader.ReadFrame();
                Assert.True(frame.IsValid);
                Assert.Equal((ulong)(i + 1), frame.Sequence);
                
                var text = Encoding.UTF8.GetString(frame.Span);
                Assert.Equal($"Frame {i}", text);
            }
        }

        [Fact]
        public void CanHandleSequentialWriteRead()
        {
            // This test verifies sequential write/read within a single process
            // For concurrent cross-process testing, see ScenarioTests
            const string bufferName = "test-buffer-sequential";
            var config = new BufferConfig(1024, 64 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            const int frameCount = 100;
            
            // Write frames
            for (int i = 0; i < frameCount; i++)
            {
                Span<byte> data = stackalloc byte[sizeof(int)];
                BitConverter.TryWriteBytes(data, i);
                writer.WriteFrame(data);
            }
            
            // Read frames
            for (int i = 0; i < frameCount; i++)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(1));
                Assert.True(frame.IsValid);
                
                var value = BitConverter.ToInt32(frame.Span);
                Assert.Equal(i, value);
            }
        }

        [Fact]
        public void CanReuseBufferNameAfterDispose()
        {
            const string bufferName = "test-buffer-reuse";
            var config = new BufferConfig(1024, 64 * 1024);
            
            // First usage
            using (var reader1 = new Reader(bufferName, config))
            using (var writer1 = new Writer(bufferName))
            {
                writer1.WriteFrame(Encoding.UTF8.GetBytes("First"));
                var frame = reader1.ReadFrame();
                Assert.Equal("First", Encoding.UTF8.GetString(frame.Span));
            }
            
            // Second usage with same name
            using (var reader2 = new Reader(bufferName, config))
            using (var writer2 = new Writer(bufferName))
            {
                writer2.WriteFrame(Encoding.UTF8.GetBytes("Second"));
                var frame = reader2.ReadFrame();
                Assert.Equal("Second", Encoding.UTF8.GetString(frame.Span));
                Assert.Equal(1UL, frame.Sequence); // Should start fresh
            }
        }

        [Fact]
        public void WriterDetectsReaderDeath()
        {
            const string bufferName = "test-buffer-death";
            var config = new BufferConfig(1024, 1024); // Small buffer
            
            var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Dispose reader to simulate death
            reader.Dispose();
            
            // Try to write - should eventually detect reader death
            Assert.Throws<ReaderDeadException>(() =>
            {
                // Fill buffer to force writer to wait
                for (int i = 0; i < 100; i++)
                {
                    writer.WriteFrame(new byte[512]);
                }
            });
        }
    }
}