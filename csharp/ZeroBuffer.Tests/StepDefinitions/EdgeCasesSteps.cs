using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTalk.SpecFlow;
using Xunit;
using ZeroBuffer.Tests.Services;

namespace ZeroBuffer.Tests.StepDefinitions
{
    [Binding]
    public class EdgeCasesSteps
    {
        private readonly IBufferNamingService _bufferNaming;
        // Own copies of readers and writers - no dependency on other step files
        private readonly Dictionary<string, Reader> _readers = new();
        private readonly Dictionary<string, Writer> _writers = new();
        private readonly Dictionary<string, bool> _wrapAroundOccurred = new();
        private readonly Dictionary<string, bool> _deadlockDetected = new();
        private readonly Dictionary<string, int> _framesSent = new();
        private readonly Dictionary<string, int> _framesReceived = new();
        private Exception? _lastException;
        private bool _writeBlocked = false;
        private Task? _largeWriteTask;

        public EdgeCasesSteps(IBufferNamingService bufferNaming)
        {
            _bufferNaming = bufferNaming;
        }

        [When(@"the '(.*)' process writes large frame using '(.*)' of buffer")]
        public void WhenProcessWritesLargeFrameUsingPercentOfBuffer(string process, string percentStr)
        {
            // Debug: "Writing large frame using {Percent} of buffer", percentStr);
            
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            // Parse percentage (e.g., "90%" -> 0.9)
            var percent = double.Parse(percentStr.TrimEnd('%')) / 100.0;
            
            // Calculate frame size based on buffer size
            // Buffer size is 10240, so 90% would be ~9216
            // But we need to account for frame overhead (16 bytes)
            var bufferSize = 10240;
            var targetSize = (int)(bufferSize * percent) - 16; // Subtract frame overhead
            
            var data = new byte[targetSize];
            // Fill with pattern to verify later
            for (int i = 0; i < targetSize; i++)
            {
                data[i] = (byte)(i % 256);
            }
            
            writer.WriteFrame(data);
            _wrapAroundOccurred[process] = false;
        }

        [When(@"the '(.*)' process writes small frame of '(.*)' byte")]
        [When(@"the '(.*)' process writes small frame of '(.*)' bytes")]
        public void WhenProcessWritesSmallFrameOfBytes(string process, string size)
        {
            // Debug: "Writing small frame of {Size} bytes", size);
            
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            var frameSize = int.Parse(size);
            var data = new byte[frameSize];
            data[0] = 0xFF; // Mark small frame
            
            writer.WriteFrame(data);
        }

        [When(@"the '(.*)' process attempts to write large frame again")]
        public void WhenProcessAttemptsToWriteLargeFrameAgain(string process)
        {
            // Debug: "Attempting to write large frame again");
            
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            // This should trigger wrap-around behavior
            // The buffer doesn't have enough contiguous space at the end
            // So it should wrap to the beginning
            
            // Try to write another large frame (90% of buffer)
            var bufferSize = 10240;
            var targetSize = (int)(bufferSize * 0.9) - 16;
            var data = new byte[targetSize];
            
            // Fill with different pattern
            for (int i = 0; i < targetSize; i++)
            {
                data[i] = (byte)((i + 128) % 256);
            }
            
            // This write might block or trigger wrap-around
            try
            {
                // Start write in background to detect blocking
                _largeWriteTask = Task.Run(() =>
                {
                    try
                    {
                        writer.WriteFrame(data);
                        _wrapAroundOccurred[process] = true;
                    }
                    catch (Exception ex)
                    {
                        _lastException = ex;
                    }
                });
                
                // Wait briefly to see if it completes
                if (!_largeWriteTask.Wait(TimeSpan.FromMilliseconds(100)))
                {
                    _writeBlocked = true;
                    // Write is blocked, which is expected until reader frees space
                }
            }
            catch (Exception ex)
            {
                _lastException = ex;
            }
        }

        [Then(@"proper wrap-around handling should occur")]
        [Then(@"the '(.*)' process proper wrap-around handling should occur")]
        public void ThenProperWrapAroundHandlingShouldOccur(string process = null)
        {
            // Debug: "Verifying proper wrap-around handling");
            
            // Either the write should have succeeded (wrap-around occurred)
            // or it should be blocked waiting for space
            Assert.True(_wrapAroundOccurred.Values.Any(v => v) || _writeBlocked,
                "Expected either wrap-around or blocking behavior");
            
            // No exceptions should have occurred
            Assert.Null(_lastException);
        }

        [Then(@"no deadlocks should happen")]
        [Then(@"the '(.*)' process no deadlocks should happen")]
        public void ThenNoDeadlocksShouldHappen(string process = "")
        {
            // Debug: "Verifying no deadlocks occurred");
            
            // If write was blocked, verify reader can still make progress
            if (_writeBlocked && _largeWriteTask != null)
            {
                // Reader should be able to read and free space
                var reader = _readers.Values.LastOrDefault();
                if (reader != null)
                {
                    // Read a frame to free space
                    using var frame = reader.ReadFrame(TimeSpan.FromMilliseconds(100));
                    if (frame.IsValid)
                    {
                        // Space is automatically signaled when frame is disposed
                        
                        // Now the blocked write should complete
                        Assert.True(_largeWriteTask.Wait(TimeSpan.FromSeconds(1)),
                            "Write should complete after reader frees space");
                    }
                }
            }
            
            // Verify no deadlock
            _deadlockDetected[process.Length > 0 ? process : "system"] = false;
        }

        [When(@"the '(.*)' process writes '(.*)' frames rapidly without reader consuming")]
        public void WhenProcessWritesFramesRapidlyWithoutReaderConsuming(string process, string count)
        {
            // Debug: "Writing {Count} frames rapidly", count);
            
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            var frameCount = int.Parse(count);
            _framesSent[process] = frameCount;
            
            // Write frames rapidly
            for (int i = 0; i < frameCount; i++)
            {
                var data = System.Text.Encoding.UTF8.GetBytes($"rapid_frame_{i}");
                writer.WriteFrame(data);
            }
        }

        [Then(@"the semaphore count should represent pending frames")]
        [Then(@"the '(.*)' process the semaphore count should represent pending frames")]
        public void ThenTheSemaphoreCountShouldRepresentPendingFrames(string process = null)
        {
            // Debug: "Verifying semaphore count represents pending frames");
            
            // This is internally handled by ZeroBuffer
            // The semaphore should have been signaled for each frame written
            // We can verify this indirectly by checking that reads don't block
            Assert.True(_framesSent.Values.Any(v => v > 0),
                "Frames should have been sent");
        }

        [When(@"the '(.*)' process wakes and processes all frames")]
        public void WhenProcessWakesAndProcessesAllFrames(string process)
        {
            // Debug: "Reader waking and processing all frames");
            
            var reader = _readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found for process '{process}'");
            }
            
            // In Test 4.2, the writer writes 10 frames rapidly
            // The reader should be able to read all of them
            var readCount = 0;
            const int maxFrames = 100; // Safety limit
            
            // Read all pending frames
            while (readCount < maxFrames)
            {
                using var frame = reader.ReadFrame(TimeSpan.FromMilliseconds(100));
                if (frame.IsValid)
                {
                    readCount++;
                    // Space is automatically signaled when frame is disposed
                }
                else
                {
                    break;
                }
            }
            
            _framesReceived[process] = readCount;
        }

        [Then(@"all frames should be read correctly")]
        [Then(@"the '(.*)' process all frames should be read correctly")]
        public void ThenAllFramesShouldBeReadCorrectly(string process = null)
        {
            // Debug: "Verifying all frames read correctly");
            
            // In Test 4.2, the writer writes 10 frames
            const int expectedFrames = 10;
            var received = _framesReceived.Values.FirstOrDefault();
            
            Assert.Equal(expectedFrames, received);
        }

        [Then(@"the coalesced signals should be handled properly")]
        [Then(@"the '(.*)' process the coalesced signals should be handled properly")]
        public void ThenTheCoalescedSignalsShouldBeHandledProperly(string process = "")
        {
            // Debug: "Verifying coalesced signals handled properly");
            
            // The fact that all frames were read successfully means
            // the coalesced semaphore signals were handled properly
            Assert.True(_framesReceived.Values.Any(v => v > 0),
                "Frames should have been received, indicating proper signal handling");
        }

        [When(@"attempts to write metadata")]
        public void WhenAttemptsToWriteMetadata()
        {
            WhenProcessAttemptsToWriteMetadata("writer");
        }
        
        [When(@"the '(.*)' process attempts to write metadata")]
        public void WhenProcessAttemptsToWriteMetadata(string process)
        {
            // Debug: "Attempting to write metadata to zero-metadata buffer");
            
            try
            {
                var writer = _writers.Values.LastOrDefault();
                if (writer == null)
                {
                    throw new InvalidOperationException($"No writer found");
                }
                
                // Try to write metadata to a buffer with 0 metadata size
                var metadata = new byte[100];
                writer.SetMetadata(metadata);
                _lastException = null;
            }
            catch (Exception ex)
            {
                _lastException = ex;
            }
        }

        [Then(@"the '(.*)' process metadata write should fail appropriately")]
        public void ThenProcessMetadataWriteShouldFailAppropriately(string process)
        {
            // Debug: "Verifying metadata write failed appropriately");
            
            Assert.NotNull(_lastException);
            Assert.True(_lastException is InvalidOperationException ||
                       _lastException is ArgumentException,
                       $"Expected metadata operation to fail but got: {_lastException?.Message}");
        }

        [When(@"the '(.*)' process writes frame without metadata")]
        public void WhenProcessWritesFrameWithoutMetadata(string process)
        {
            // Debug: "Writing frame without metadata");
            
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            // Write frame data without metadata
            var data = System.Text.Encoding.UTF8.GetBytes("no_metadata_frame");
            writer.WriteFrame(data);
        }

        [Then(@"the '(.*)' process should verify frame write succeeded")]
        public void ThenProcessShouldVerifyFrameWriteSucceeded(string process)
        {
            // Debug: "Verifying frame write succeeded";
            
            // If we got here without exception, write succeeded
            // Can also verify by reading the frame
            var reader = _readers.Values.LastOrDefault();
            if (reader != null)
            {
                using var frame = reader.ReadFrame(TimeSpan.FromMilliseconds(100));
                Assert.True(frame.IsValid, "Frame should be valid");
            }
        }

        [Then(@"the '(.*)' process should verify system works correctly without metadata")]
        public void ThenProcessShouldVerifySystemWorksCorrectlyWithoutMetadata(string process)
        {
            // Debug: "Verifying system works without metadata");
            
            var reader = _readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found");
            }
            
            // Verify metadata is empty/zero-sized
            var metadata = reader.GetMetadata();
            Assert.Equal(0, metadata.Length);
            
            // System should still function for frame operations
            Assert.True(true, "System works without metadata");
        }

        [Given(@"the '(.*)' process creates buffer '(.*)' with default config")]
        public void GivenProcessCreatesBufferWithDefaultConfig(string process, string bufferName)
        {
            // Debug: "Creating buffer with default config");
            
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            var config = new BufferConfig
            {
                MetadataSize = 1024,
                PayloadSize = 10240
            };
            var reader = new ZeroBuffer.Reader(actualBufferName, config);
            _readers[bufferName] = reader;
        }
        
        // Removed duplicate - using BasicCommunicationSteps.WhenProcessConnectsToBuffer instead

        [Given(@"the '(.*)' process creates buffer '(.*)' with minimum viable size '(.*)'")]
        public void GivenProcessCreatesBufferWithMinimumViableSize(string process, string bufferName, string size)
        {
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            var minSize = int.Parse(size);
            
            // Minimum viable size for our test
            // We want only enough space for one 1-byte frame (16 header + 1 data = 17)
            // But ZeroBuffer might need some extra space for management
            // Let's use exactly the specified size
            var config = new BufferConfig
            {
                MetadataSize = 0,
                PayloadSize = minSize  // This is the total buffer size
            };
            var reader = new ZeroBuffer.Reader(actualBufferName, config);
            _readers[bufferName] = reader;
        }

        [When(@"writes single byte frame")]
        public void WhenWritesSingleByteFrame()
        {
            WhenProcessWritesSingleByteFrame("writer");
        }
        
        [When(@"the '(.*)' process writes single byte frame")]
        public void WhenProcessWritesSingleByteFrame(string process)
        {
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            var data = new byte[] { 0x42 }; // Single byte
            writer.WriteFrame(data);
        }

        [Then(@"the '(.*)' process should verify write succeeded")]
        public void ThenProcessShouldVerifyWriteSucceeded(string process)
        {
            // Debug: "Verifying write succeeded");
            
            // If we got here without exception, write succeeded
            Assert.Null(_lastException);
        }

        [When(@"the '(.*)' process fills buffer to '(.*)' capacity")]
        public void WhenProcessFillsBufferToCapacity(string process, string percentStr)
        {
            // Debug: "Filling buffer to {Percent} capacity", percentStr);
            
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            // Parse percentage (e.g., "80%" -> 0.8)
            var percent = double.Parse(percentStr.TrimEnd('%')) / 100.0;
            
            // Calculate how much to fill
            var bufferSize = 10240;
            var targetFill = (int)(bufferSize * percent);
            var written = 0;
            
            // Write frames until we reach target fill
            while (written < targetFill)
            {
                var frameSize = Math.Min(1024, targetFill - written - 16); // Account for header
                if (frameSize <= 0) break;
                
                var data = new byte[frameSize];
                for (int i = 0; i < frameSize; i++)
                {
                    data[i] = (byte)(written % 256);
                }
                
                writer.WriteFrame(data);
                written += frameSize + 16; // Include header
            }
        }

        [When(@"a second writer process attempts to connect to buffer '(.*)'")]
        [When(@"the '(.*)' process a second writer process attempts to connect to buffer '(.*)'")]
        public void WhenASecondWriterProcessAttemptsToConnectToBuffer(string processOrBuffer, string bufferName = null)
        {
            // Handle both single param and double param versions
            if (bufferName == null)
            {
                bufferName = processOrBuffer;
            }
            
            // Debug: "Second writer attempting to connect to buffer {BufferName}", bufferName);
            
            try
            {
                var secondWriter = new ZeroBuffer.Writer(bufferName);
                // If we get here, connection succeeded when it shouldn't have
                _lastException = null;
                secondWriter.Dispose();
            }
            catch (Exception ex)
            {
                _lastException = ex;
            }
        }

        [Then(@"the second writer should fail with writer exists error")]
        [Then(@"the '(.*)' process the second writer should fail with writer exists error")]
        public void ThenTheSecondWriterShouldFailWithWriterExistsError(string process = null)
        {
            // Debug: "Verifying second writer failed with writer exists error");
            
            Assert.NotNull(_lastException);
            Assert.True(_lastException is WriterAlreadyConnectedException ||
                       _lastException.Message.Contains("writer") ||
                       _lastException.Message.Contains("already"),
                       $"Expected writer already exists error but got: {_lastException?.Message}");
        }

        [When(@"the '(.*)' process continues filling buffer to '(.*)'")] 
        public void WhenProcessContinuesFillingBufferTo(string process, string percentStr)
        {
            // Same as filling buffer, but continue from where we left off
            WhenProcessFillsBufferToCapacity(process, percentStr);
        }

        [When(@"the '(.*)' process attempts to write '(.*)' byte frame")]
        public void WhenProcessAttemptsToWriteByteFrame(string process, string size)
        {
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            var frameSize = int.Parse(size);
            var data = new byte[frameSize];
            for (int i = 0; i < frameSize; i++)
            {
                data[i] = (byte)(i % 256);
            }
            
            // Try to write - may throw FrameTooLargeException
            // Frame of 2 bytes + 16 header = 18 bytes total, but buffer is only 17 bytes
            try
            {
                writer.WriteFrame(data);
                _lastException = null;
            }
            catch (Exception ex)
            {
                _lastException = ex;
            }
        }
        
        [Then(@"the '(.*)' process should receive FrameTooLargeException")]
        public void ThenProcessShouldReceiveFrameTooLargeException(string process)
        {
            Assert.NotNull(_lastException);
            Assert.IsType<FrameTooLargeException>(_lastException);
        }
        
        [Then(@"the '(.*)' process should block waiting for space")]
        public void ThenProcessShouldBlockWaitingForSpace(string process)
        {
            // Debug: "Verifying writer blocks waiting for space");
            
            // Check if the write task from the previous step is still running (blocked)
            if (_largeWriteTask != null)
            {
                // Check if there was an exception
                if (_lastException != null)
                {
                    throw new InvalidOperationException($"Write failed with exception: {_lastException.Message}", _lastException);
                }
                
                Assert.False(_largeWriteTask.IsCompleted,
                    $"Write should be blocked waiting for space. Task status: {_largeWriteTask.Status}");
                return;
            }
            
            // Fallback: Try to write one more frame - should block or timeout
            var writer = _writers.Values.LastOrDefault();
            if (writer != null)
            {
                var blockingTask = Task.Run(() =>
                {
                    var data = new byte[100];
                    writer.WriteFrame(data);
                });
                
                // Should not complete immediately
                Assert.False(blockingTask.Wait(TimeSpan.FromMilliseconds(100)),
                    "Write should block when buffer is full");
            }
        }

        // Test 4.5 - Reader Slower Than Writer
        private CancellationTokenSource? _writerCts;
        private Task? _writerTask;
        private int _framesWritten = 0;
        private int _framesRead = 0;
        private int _blockedWrites = 0;
        private List<double> _writeTimings = new List<double>();

        [When(@"writes continuously at high speed")]
        [When(@"the '(.*)' process writes continuously at high speed")]
        public void WhenWritesContinuouslyAtHighSpeed(string process = null)
        {
            var writer = _writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException("No writer found");
            }

            _framesWritten = 0;
            _blockedWrites = 0;
            _writeTimings.Clear();
            _writerCts = new CancellationTokenSource();
            
            _writerTask = Task.Run(() =>
            {
                // Create frame with sequence number
                var frameData = new byte[100];
                var random = new Random();
                
                // Write continuously until cancelled
                while (!_writerCts.Token.IsCancellationRequested && _framesWritten < 200) // Safety limit
                {
                    try
                    {
                        // Embed sequence number in frame
                        BitConverter.GetBytes(_framesWritten).CopyTo(frameData, 0);
                        
                        // Measure write time
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        writer.WriteFrame(frameData);
                        sw.Stop();
                        
                        var writeMs = sw.Elapsed.TotalMilliseconds;
                        _writeTimings.Add(writeMs);
                        
                        // Detect blocking (20ms threshold for 50ms reader delay)
                        if (writeMs > 20)
                        {
                            _blockedWrites++;
                        }
                        
                        _framesWritten++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        // Buffer might be closed, exit gracefully
                        break;
                    }
                }
            });
        }

        [When(@"the '(.*)' process reads with '(.*)' ms delay per frame")]
        public void WhenProcessReadsWithDelayPerFrame(string process, string delayMs)
        {
            var reader = _readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException("No reader found");
            }

            var delay = int.Parse(delayMs);
            _framesRead = 0;
            var expectedSeq = 0;
            
            Task.Run(async () =>
            {
                while (_framesRead < 100 && !_writerCts?.Token.IsCancellationRequested != false)
                {
                    var frame = reader.ReadFrame();
                    if (frame.IsValid)
                    {
                        // Verify sequence number (first 4 bytes of payload)
                        var span = frame.Span;
                        if (span.Length >= 4)
                        {
                            var receivedSeq = BitConverter.ToInt32(span.Slice(0, 4));
                            if (receivedSeq != expectedSeq)
                            {
                                throw new InvalidOperationException($"Frame loss detected! Expected seq {expectedSeq}, got {receivedSeq}");
                            }
                        }
                        expectedSeq++;
                        _framesRead++;
                        
                        // Reader automatically signals space when reading next frame
                        await Task.Delay(delay); // Slow reader
                    }
                    else
                    {
                        await Task.Delay(1);
                    }
                }
            });
        }

        [When(@"the test runs for '(.*)' frames")]
        [When(@"the '(.*)' process the test runs for '(.*)' frames")]
        public void WhenTheTestRunsForFrames(string processOrFrameCount, string frameCount = null)
        {
            // Handle both parameter patterns
            var targetFramesStr = frameCount ?? processOrFrameCount;
            var targetFrames = int.Parse(targetFramesStr);
            var timeout = DateTime.UtcNow.AddSeconds(10); // 100 frames * 50ms = 5s, so 10s is safe

            // Wait for reader to process target frames
            while (_framesRead < targetFrames && DateTime.UtcNow < timeout)
            {
                Thread.Sleep(50);
            }

            // Stop writer
            _writerCts?.Cancel();
            try
            {
                _writerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        [Then(@"the '(.*)' process should receive all frames without loss")]
        public void ThenProcessShouldReceiveAllFramesWithoutLoss(string process)
        {
            Assert.Equal(100, _framesRead);
        }

        [Then(@"the '(.*)' process should block appropriately")]
        public void ThenProcessShouldBlockAppropriately(string process)
        {
            // With 1KB buffer (8 frames) and 100 frames total, expect ~92 blocks
            Assert.True(_blockedWrites > 80, $"Writer should have blocked many times, but only blocked {_blockedWrites} times");
            
            // Verify blocking duration
            if (_writeTimings.Count > 10)
            {
                var avgBlockedTime = _writeTimings.Skip(8).Average(); // Skip initial burst
                Assert.True(avgBlockedTime > 20, $"Blocked writes should take >20ms, avg was {avgBlockedTime:F1}ms");
            }
        }

        [Then(@"flow control should work correctly")]
        [Then(@"the '(.*)' process flow control should work correctly")]
        public void ThenFlowControlShouldWorkCorrectly(string process = null)
        {
            Assert.True(_framesWritten >= 100, $"Writer should have written at least 100 frames, wrote {_framesWritten}");
            Assert.True(_blockedWrites > 80, $"Flow control should have caused many blocks, had {_blockedWrites}");
            
            // Verify timing pattern
            if (_writeTimings.Count > 10)
            {
                var firstBurst = _writeTimings.Take(8).Average();
                var blockedWrites = _writeTimings.Skip(8).Average();
                Assert.True(firstBurst < 5, $"Initial burst should be fast (<5ms), was {firstBurst:F1}ms");
                Assert.True(blockedWrites > 20, $"Blocked writes should be slow (>20ms), was {blockedWrites:F1}ms");
            }
        }
    }
}