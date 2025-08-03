using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Tests
{
    public class ScenarioTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly List<string> _buffersToCleanup = new();

        public ScenarioTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            // Cleanup any test buffers
            foreach (var bufferName in _buffersToCleanup)
            {
                try
                {
                    // Try to clean up shared memory and semaphores
                    // In a real implementation, we'd have a cleanup method
                }
                catch { }
            }
        }

        private string CreateTestBufferName(string prefix)
        {
            var name = $"{prefix}-{Guid.NewGuid():N}";
            _buffersToCleanup.Add(name);
            return name;
        }

        [Fact]
        public async Task Test_1_1_SimpleWriteReadCycle()
        {
            _output.WriteLine("=== Test 1.1: Simple Write-Read Cycle (Cross-Process) ===");
            
            var bufferName = CreateTestBufferName("scenario-simple-xproc");
            var config = new BufferConfig(1024, 64 * 1024);
            
            // Create test data and calculate hash
            var testData = Encoding.UTF8.GetBytes("Hello, ZeroBuffer from another process!");
            var expectedHash = Convert.ToBase64String(SHA256.HashData(testData));
            
            // Start reader in separate process
            var readerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = GetHelperProjectPath(),
                Arguments = $"reader-with-validation {bufferName} {config.MetadataSize} {config.PayloadSize}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            
            Assert.NotNull(readerProcess);
            
            // Wait for reader to start and create buffer
            await Task.Delay(1000);
            
            // Connect writer in main process
            using var writer = new Writer(bufferName);
            
            // Write test data
            writer.WriteFrame(testData);
            _output.WriteLine($"✓ Wrote frame with {testData.Length} bytes, hash: {expectedHash}");
            
            // Write a second frame to signal reader to exit
            writer.WriteFrame(Encoding.UTF8.GetBytes("EXIT"));
            
            // Wait for reader process to complete
            await readerProcess.WaitForExitAsync();
            
            // Check reader output
            var output = await readerProcess.StandardOutput.ReadToEndAsync();
            _output.WriteLine($"Reader output: {output}");
            
            Assert.Contains($"Read frame 1, size: {testData.Length}", output);
            Assert.Contains($"Hash: {expectedHash}", output);
            Assert.Equal(0, readerProcess.ExitCode);
            
            _output.WriteLine("✓ Successfully wrote and read frames across processes with hash validation");
        }

        [Fact]
        public void Test_1_2_MultipleFramesSequential()
        {
            _output.WriteLine("=== Test 1.2: Multiple Frames Sequential ===");
            
            var bufferName = CreateTestBufferName("scenario-multi");
            var config = new BufferConfig(1024, 256 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            const int frameCount = 100;
            
            // Write multiple frames
            for (int i = 0; i < frameCount; i++)
            {
                Span<byte> data = stackalloc byte[sizeof(int)];
                BitConverter.TryWriteBytes(data, i);
                writer.WriteFrame(data);
            }
            
            // Read all frames
            for (int i = 0; i < frameCount; i++)
            {
                var frame = reader.ReadFrame();
                Assert.True(frame.IsValid);
                Assert.Equal((ulong)(i + 1), frame.Sequence);
                
                var value = BitConverter.ToInt32(frame.Span);
                Assert.Equal(i, value);
            }
            
            _output.WriteLine($"✓ Successfully wrote and read {frameCount} frames sequentially");
        }

        [Fact]
        public void Test_1_3_BufferFullHandling()
        {
            _output.WriteLine("=== Test 1.3: Buffer Full Handling ===");
            
            var bufferName = CreateTestBufferName("scenario-full");
            var config = new BufferConfig(256, 4096); // Small buffer
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Fill buffer without reading
            var frameData = new byte[1024]; // Large frame
            Random.Shared.NextBytes(frameData);
            
            int framesWritten = 0;
            bool gotBufferFull = false;
            
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    writer.WriteFrame(frameData);
                    framesWritten++;
                }
            }
            catch (BufferFullException)
            {
                gotBufferFull = true;
                _output.WriteLine($"✓ Got BufferFullException after {framesWritten} frames");
            }
            
            Assert.True(gotBufferFull);
            Assert.True(framesWritten < 10); // Should not fit all frames
            
            // Read one frame to make space
            var frame = reader.ReadFrame();
            Assert.True(frame.IsValid);
            
            // Should be able to write again
            writer.WriteFrame(frameData);
            _output.WriteLine("✓ Successfully wrote after freeing space");
        }

        [Fact]
        public async Task Test_2_1_WriterCrashDetection()
        {
            _output.WriteLine("=== Test 2.1: Writer Crash Detection ===");
            
            var bufferName = CreateTestBufferName("scenario-writer-crash");
            var config = new BufferConfig(1024, 64 * 1024);
            
            using var reader = new Reader(bufferName, config);
            
            // Start writer in separate process
            var writerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = GetHelperProjectPath(),
                Arguments = $"writer {bufferName}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            Assert.NotNull(writerProcess);
            
            // Wait for writer to connect and write some frames
            await Task.Delay(500);
            
            // Read a frame to verify writer is working
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(2));
            Assert.True(frame.IsValid);
            
            // Kill writer process
            writerProcess.Kill();
            await writerProcess.WaitForExitAsync();
            
            // Try to read - should detect writer death
            await Assert.ThrowsAsync<WriterDeadException>(async () =>
            {
                await Task.Run(() => reader.ReadFrame(TimeSpan.FromSeconds(2)));
            });
            
            _output.WriteLine("✓ Successfully detected writer crash");
        }

        [Fact]
        public async Task Test_2_2_ReaderCrashDetection()
        {
            _output.WriteLine("=== Test 2.2: Reader Crash Detection ===");
            
            var bufferName = CreateTestBufferName("scenario-reader-crash");
            var config = new BufferConfig(256, 4096); // Small buffer to force waiting
            
            // Start reader in separate process
            var readerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = GetHelperProjectPath(),
                Arguments = $"reader {bufferName} {config.MetadataSize} {config.PayloadSize}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            Assert.NotNull(readerProcess);
            
            // Wait for reader to start
            await Task.Delay(500);
            
            using var writer = new Writer(bufferName);
            
            // Write until buffer is full
            var frameData = new byte[1024];
            for (int i = 0; i < 5; i++)
            {
                writer.WriteFrame(frameData);
            }
            
            // Kill reader process
            readerProcess.Kill();
            await readerProcess.WaitForExitAsync();
            
            // Try to write more - should detect reader death
            await Assert.ThrowsAsync<ReaderDeadException>(async () =>
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        writer.WriteFrame(frameData);
                    }
                });
            });
            
            _output.WriteLine("✓ Successfully detected reader crash");
        }

        [Fact]
        public void Test_4_1_MetadataWriteOnce()
        {
            _output.WriteLine("=== Test 4.1: Metadata Write-Once ===");
            
            var bufferName = CreateTestBufferName("scenario-metadata-once");
            var config = new BufferConfig(1024, 64 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Write metadata
            var metadata = Encoding.UTF8.GetBytes("Camera Config v1.0");
            writer.SetMetadata(metadata);
            
            // Try to write metadata again - should fail
            Assert.Throws<InvalidOperationException>(() =>
            {
                writer.SetMetadata(metadata);
            });
            
            // Read metadata
            var readMetadata = reader.GetMetadata();
            Assert.Equal(metadata, readMetadata.ToArray());
            
            _output.WriteLine("✓ Metadata write-once constraint enforced");
        }

        [Fact]
        public void Test_4_2_MetadataSizeValidation()
        {
            _output.WriteLine("=== Test 4.2: Metadata Size Validation ===");
            
            var bufferName = CreateTestBufferName("scenario-metadata-size");
            var config = new BufferConfig(256, 64 * 1024); // Small metadata buffer
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Try to write metadata larger than buffer
            var largeMetadata = new byte[512];
            Random.Shared.NextBytes(largeMetadata);
            
            Assert.Throws<ArgumentException>(() =>
            {
                writer.SetMetadata(largeMetadata);
            });
            
            // Write metadata that fits
            var smallMetadata = new byte[128];
            Random.Shared.NextBytes(smallMetadata);
            writer.SetMetadata(smallMetadata);
            
            var readMetadata = reader.GetMetadata();
            Assert.Equal(smallMetadata, readMetadata.ToArray());
            
            _output.WriteLine("✓ Metadata size validation working correctly");
        }

        [Fact]
        public void Test_5_3_WrapAroundBehavior()
        {
            _output.WriteLine("=== Test 5.3: Wrap-Around Behavior ===");
            
            var bufferName = CreateTestBufferName("scenario-wrap");
            var config = new BufferConfig(512, 8192); // Small buffer to force wrap
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            var frameSize = 1024;
            var frameData = new byte[frameSize];
            
            // Write and read many frames to force wrap-around
            for (int i = 0; i < 50; i++)
            {
                frameData[0] = (byte)i;
                writer.WriteFrame(frameData);
                
                var frame = reader.ReadFrame();
                Assert.True(frame.IsValid);
                Assert.Equal((ulong)(i + 1), frame.Sequence);
                Assert.Equal(i, frame.Span[0]);
            }
            
            _output.WriteLine("✓ Buffer wrap-around handled correctly");
        }

        [Fact]
        public void Test_6_1_RapidWriteReadCycles()
        {
            _output.WriteLine("=== Test 6.1: Rapid Write-Read Cycles ===");
            
            var bufferName = CreateTestBufferName("scenario-rapid");
            var config = new BufferConfig(1024, 64 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            const int cycles = 1000;
            var sw = Stopwatch.StartNew();
            
            for (int i = 0; i < cycles; i++)
            {
                Span<byte> data = stackalloc byte[sizeof(int)];
                BitConverter.TryWriteBytes(data, i);
                writer.WriteFrame(data);
                
                var frame = reader.ReadFrame();
                Assert.True(frame.IsValid);
                
                var value = BitConverter.ToInt32(frame.Span);
                Assert.Equal(i, value);
            }
            
            sw.Stop();
            var throughput = cycles / sw.Elapsed.TotalSeconds;
            
            _output.WriteLine($"✓ Completed {cycles} cycles in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Throughput: {throughput:F0} frames/second");
        }

        [Fact]
        public void Test_7_1_PatternValidation()
        {
            _output.WriteLine("=== Test 7.1: Pattern Validation with Hash ===");
            
            var bufferName = CreateTestBufferName("scenario-pattern");
            var config = new BufferConfig(1024, 256 * 1024);
            
            using var reader = new Reader(bufferName, config);
            using var writer = new Writer(bufferName);
            
            // Create pattern data
            byte GeneratePattern(int frame, int offset) => (byte)((frame * 7 + offset * 13) % 256);
            
            const int frameCount = 100;
            const int frameSize = 1024;
            
            // Store hashes for validation
            var frameHashes = new Dictionary<int, string>();
            
            // Write all frames first
            for (int i = 0; i < frameCount; i++)
            {
                var data = new byte[frameSize];
                for (int j = 0; j < frameSize; j++)
                {
                    data[j] = GeneratePattern(i, j);
                }
                
                // Calculate and store hash
                frameHashes[i] = Convert.ToBase64String(SHA256.HashData(data));
                writer.WriteFrame(data);
            }
            
            // Read and verify all frames
            var receivedFrames = new HashSet<int>();
            for (int i = 0; i < frameCount; i++)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                Assert.True(frame.IsValid, $"Frame {i} is not valid");
                
                var span = frame.Span;
                Assert.Equal(frameSize, span.Length);
                
                // Verify the pattern matches expected for this frame
                var expectedFirst = GeneratePattern(i, 0);
                var expectedLast = GeneratePattern(i, frameSize - 1);
                Assert.Equal(expectedFirst, span[0]);
                Assert.Equal(expectedLast, span[frameSize - 1]);
                
                // Sample check in the middle
                var expectedMiddle = GeneratePattern(i, frameSize / 2);
                Assert.Equal(expectedMiddle, span[frameSize / 2]);
                
                // Verify hash
                var actualHash = Convert.ToBase64String(SHA256.HashData(span));
                Assert.Equal(frameHashes[i], actualHash);
                
                receivedFrames.Add(i);
            }
            
            Assert.Equal(frameCount, receivedFrames.Count);
            
            _output.WriteLine($"✓ Pattern validation with hash verification passed for {frameCount} frames");
        }

        private static string GetHelperProjectPath()
        {
            // Get the compiled executable path
            var assemblyLocation = typeof(ScenarioTests).Assembly.Location;
            var directory = Path.GetDirectoryName(assemblyLocation)!;
            var helperExe = Path.Combine(directory, "ZeroBuffer.TestHelper");
            
            // On Windows, add .exe extension
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                helperExe += ".exe";
                
            return helperExe;
        }
    }
}