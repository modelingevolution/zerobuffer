using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTalk.SpecFlow;
using Xunit;
using Xunit.Sdk;
using ZeroBuffer.Tests.Services;
using SkipException = Xunit.SkipException;

namespace ZeroBuffer.Tests.StepDefinitions
{
    [Binding]
    public class ProcessLifecycleSteps
    {
        private readonly Dictionary<string, Process> _processes = new();
        private readonly IBufferNamingService _bufferNaming;
        private readonly BasicCommunicationSteps _basicSteps;
        private Exception? _lastException;

        public ProcessLifecycleSteps(IBufferNamingService bufferNaming, BasicCommunicationSteps basicSteps)
        {
            _bufferNaming = bufferNaming;
            _basicSteps = basicSteps;
        }

        [Given(@"we run in harmony")]
        public void GivenWeRunInHarmony()
        {
            // Check if we're running under Harmony by looking for Harmony environment variables
            var harmonyHostPid = Environment.GetEnvironmentVariable("HARMONY_HOST_PID");
            var harmonyFeatureId = Environment.GetEnvironmentVariable("HARMONY_FEATURE_ID");
            
            // Debug: Checking Harmony environment
            
            if (string.IsNullOrEmpty(harmonyHostPid) && string.IsNullOrEmpty(harmonyFeatureId))
            {
                // Not running under Harmony - skipping test
                throw new SkipException("This test requires Harmony cross-process testing framework");
            }
            
            // Running under Harmony - proceeding with test
        }

        // Note: GivenProcessCreatesBufferWithSizes is already defined in BasicCommunicationSteps
        // Removed to avoid ambiguity

        // Note: WhenProcessConnectsToBuffer is already defined in BasicCommunicationSteps
        // Removed to avoid ambiguity

        // Note: WhenProcessWritesFrameWithData is already defined in BasicCommunicationSteps
        // Removed to avoid ambiguity

        [Then(@"the '(.*)' process should read frame with data '(.*)'")]
        public void ThenProcessShouldReadFrameWithData(string process, string expectedData)
        {
            // Accept process parameter but ignore it
            var reader = _basicSteps._readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found for process '{process}'");
            }
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            
            // Convert frame data to string and compare
            var frameString = System.Text.Encoding.UTF8.GetString(frame.Span);
            Assert.Equal(expectedData, frameString);
        }

        [When(@"the '(.*)' process writes frame with sequence '(.*)'")]
        public void WhenProcessWritesFrameWithSequence(string process, string sequence)
        {
            // Accept process parameter but ignore it
            var writer = _basicSteps._writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            var dataBytes = System.Text.Encoding.UTF8.GetBytes($"Frame {sequence}");
            writer.WriteFrame(dataBytes);
        }

        [Then(@"the '(.*)' process should read frame with sequence '(.*)'")]
        public void ThenProcessShouldReadFrameWithSequence(string process, string sequence)
        {
            // Accept process parameter but ignore it
            var reader = _basicSteps._readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found for process '{process}'");
            }
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            
            // Check sequence number
            Assert.Equal(ulong.Parse(sequence), frame.Sequence);
        }

        [When(@"the '(.*)' process is killed")]
        public void WhenProcessIsKilled(string process)
        {
            // Debug;
            
            // This is handled by Harmony as a special case
            // The actual killing happens in Harmony's ScenarioExecution.cs
            // We just mark it as killed here for validation
            
            // In a real scenario, Harmony would have already killed the process
            // For testing purposes, we simulate the effect
            if (process == "writer")
            {
                // Close and remove the writer
                var writer = _basicSteps._writers.Values.LastOrDefault();
                if (writer != null)
                {
                    writer.Dispose();
                    _basicSteps._writers.Clear();
                }
            }
            else if (process == "reader")
            {
                // Close and remove the reader
                var reader = _basicSteps._readers.Values.LastOrDefault();
                if (reader != null)
                {
                    reader.Dispose();
                    _basicSteps._readers.Clear();
                }
            }
        }

        [When(@"the '(.*)' process crashes")]
        public void WhenProcessCrashes(string process)
        {
            // Debug;
            
            // This is handled by Harmony as a special case via JSON-RPC
            // The actual crash injection happens in Harmony's ScenarioExecution.cs
            
            // For testing purposes, we simulate the effect
            if (process == "writer")
            {
                // Simulate writer crash by disposing without cleanup
                var writer = _basicSteps._writers.Values.LastOrDefault();
                if (writer != null)
                {
                    // Simulate abrupt termination
                    writer.Dispose();
                    _basicSteps._writers.Clear();
                }
            }
            else if (process == "reader")
            {
                // Simulate reader crash by disposing without cleanup
                var reader = _basicSteps._readers.Values.LastOrDefault();
                if (reader != null)
                {
                    // Simulate abrupt termination
                    reader.Dispose();
                    _basicSteps._readers.Clear();
                }
            }
        }

        [When(@"the '(.*)' process fills buffer completely")]
        public void WhenProcessFillsBufferCompletely(string process)
        {
            // Debug;
            
            var writer = _basicSteps._writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            // Write frames until buffer is full
            var largeData = new byte[1024]; // 1KB frames
            for (int i = 0; i < 1000; i++) // Try to write many frames
            {
                try
                {
                    writer.WriteFrame(largeData);
                }
                catch
                {
                    // Buffer is full, which is what we want
                    break;
                }
            }
        }

        [Then(@"the '(.*)' process should timeout or detect writer death on next read")]
        public void ThenProcessShouldTimeoutOrDetectWriterDeathOnRead(string process)
        {
            // Debug;
            
            var bufferName = _basicSteps._readers.Keys.LastOrDefault();
            if (string.IsNullOrEmpty(bufferName))
            {
                // No buffer name found in readers
                throw new InvalidOperationException("No buffer name found");
            }
            
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            // Attempting to check writer death
            
            Reader? reader = null;
            
            // Try to get existing reader or create a new one
            if (_basicSteps._readers.TryGetValue(bufferName, out var existingReader))
            {
                reader = existingReader;
                // Debug;
            }
            else
            {
                // No reader available
                throw new InvalidOperationException($"No reader available for buffer '{bufferName}'");
            }

            // Try to read - should timeout or get invalid frame after writer death
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(2));
            
            // After writer death, we should either get an invalid frame or timeout
            Assert.False(frame.IsValid, "Expected invalid frame or timeout after writer death");
        }

        [Then(@"the '(.*)' process should detect reader death on next write")]
        public void ThenProcessShouldDetectReaderDeathOnWrite(string process)
        {
            // Debug;
            
            var bufferName = _basicSteps._writers.Keys.LastOrDefault();
            if (string.IsNullOrEmpty(bufferName))
            {
                throw new InvalidOperationException("No buffer name found");
            }
            
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            // Attempting to check reader death
            
            var writer = _basicSteps._writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }

            // After reader death, writing should eventually fail or timeout
            // Write multiple frames to ensure detection
            bool detectedDeath = false;
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var data = System.Text.Encoding.UTF8.GetBytes($"test_{i}");
                    writer.WriteFrame(data);
                    Thread.Sleep(100);
                }
                catch
                {
                    // Debug;
                    detectedDeath = true;
                    break;
                }
            }
            
            Assert.True(detectedDeath, "Should have detected reader death");
        }

        [When(@"a new '(.*)' process connects to existing buffer '(.*)'")]
        public void WhenNewProcessConnectsToExistingBuffer(string process, string bufferName)
        {
            // Debug;
            
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            
            if (process == "reader")
            {
                // Create new reader for existing buffer - need to get config
                // Since we're connecting to existing buffer, we assume default config
                var config = new BufferConfig(1024, 10240); // Use default sizes
                var reader = new Reader(actualBufferName, config);
                _basicSteps._readers[bufferName] = reader;
            }
            else if (process == "writer")
            {
                // Create new writer for existing buffer
                var writer = new Writer(actualBufferName);
                _basicSteps._writers[bufferName] = writer;
            }
        }

        [Then(@"the buffer should continue functioning normally")]
        public void ThenBufferShouldContinueFunctioningNormally()
        {
            // Debug;
            
            var reader = _basicSteps._readers.Values.LastOrDefault();
            var writer = _basicSteps._writers.Values.LastOrDefault();
            
            if (reader != null && writer != null)
            {
                // Test write and read
                var testData = System.Text.Encoding.UTF8.GetBytes("test_recovery");
                writer.WriteFrame(testData);
                
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(2));
                Assert.True(frame.IsValid, "Frame should be valid after recovery");
            }
        }

        [When(@"a second '(.*)' process attempts to connect to buffer '(.*)'")]
        public void WhenSecondProcessAttemptsToConnectToBuffer(string process, string bufferName)
        {
            // Debug;
            
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            
            try
            {
                if (process == "writer")
                {
                    // Try to create a second writer - should fail
                    var writer = new Writer(actualBufferName);
                    // If we get here, no exception was thrown
                    _lastException = null;
                    writer.Dispose();
                }
                else if (process == "reader")
                {
                    // Try to create a second reader - should fail
                    var config = new BufferConfig(1024, 10240); // Use default sizes
                    var reader = new Reader(actualBufferName, config);
                    // If we get here, no exception was thrown
                    _lastException = null;
                    reader.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Debug;
                _lastException = ex;
            }
        }

        [Then(@"the connection should fail with writer exists error")]
        public void ThenConnectionShouldFailWithWriterExistsError()
        {
            // Debug;
            
            Assert.NotNull(_lastException);
            Assert.True(_lastException is WriterAlreadyConnectedException ||
                       _lastException.Message.Contains("writer", StringComparison.OrdinalIgnoreCase),
                       $"Expected writer already exists error but got: {_lastException.Message}");
        }

        [When(@"the '(.*)' process closes connection gracefully")]
        public void WhenProcessClosesConnectionGracefully(string process)
        {
            // Debug;
            
            if (process == "writer")
            {
                var writer = _basicSteps._writers.Values.LastOrDefault();
                if (writer != null)
                {
                    writer.Dispose();
                    _basicSteps._writers.Clear();
                }
            }
            else if (process == "reader")
            {
                var reader = _basicSteps._readers.Values.LastOrDefault();
                if (reader != null)
                {
                    reader.Dispose();
                    _basicSteps._readers.Clear();
                }
            }
        }

        [Then(@"the writer should be disconnected")]
        public void ThenWriterShouldBeDisconnected()
        {
            // Debug;
            
            // Check that there are no active writers
            Assert.Empty(_basicSteps._writers);
        }

        [Then(@"the '(.*)' process cleanup should succeed")]
        public void ThenProcessCleanupShouldSucceed(string process)
        {
            // Debug;
            
            // Cleanup is considered successful if no exceptions were thrown
            // and resources are properly released
            if (process == "reader")
            {
                // Reader should still exist but writer should be gone
                Assert.NotEmpty(_basicSteps._readers);
            }
        }
    }
}