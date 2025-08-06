using System;
using System.Collections.Generic;
using System.Linq;
using TechTalk.SpecFlow;
using Xunit;
using ZeroBuffer.Tests.Services;

namespace ZeroBuffer.Tests.StepDefinitions
{
    [Binding]
    public class ErrorHandlingSteps
    {
        private readonly Dictionary<string, byte[]> _metadata = new();
        private readonly Dictionary<string, byte[]> _originalMetadata = new();
        private readonly IBufferNamingService _bufferNaming;
        private readonly BasicCommunicationSteps _basicSteps;
        private string _currentBuffer = "";
        private Exception? _lastException;

        public ErrorHandlingSteps(IBufferNamingService bufferNaming, BasicCommunicationSteps basicSteps)
        {
            _bufferNaming = bufferNaming;
            _basicSteps = basicSteps;
        }

        // Note: WhenProcessWritesMetadataWithSize is already defined in BasicCommunicationSteps
        // We'll hook into that and store metadata for verification
        
        [AfterStep]
        public void AfterMetadataWrite()
        {
            // If metadata was just written, capture it for verification
            if (_basicSteps._writers.Count > 0)
            {
                _currentBuffer = _basicSteps._writers.Keys.LastOrDefault() ?? _basicSteps._readers.Keys.LastOrDefault() ?? "";
                // Store metadata if it was written (we'll check in verification steps)
            }
        }

        // Note: WhenProcessWritesFrameWithData is already defined in BasicCommunicationSteps and ProcessLifecycleSteps
        // Removed to avoid ambiguity

        [When(@"the '(.*)' process attempts to write metadata again with size '(.*)'")]
        public void WhenProcessAttemptsToWriteMetadataAgain(string process, string size)
        {
            // Debug: "Attempting to write metadata again with size {Size}", size);
            
            try
            {
                // Get the writer for this buffer
                var writer = _basicSteps._writers.Values.LastOrDefault();
                if (writer == null)
                {
                    throw new InvalidOperationException($"No writer found for process '{process}'");
                }
                
                var metadataSize = int.Parse(size);
                var metadata = new byte[metadataSize];
                
                // Fill with different test data
                for (int i = 0; i < metadataSize; i++)
                {
                    metadata[i] = (byte)((i + 100) % 256);
                }
                
                writer.SetMetadata(metadata);
                
                // If we get here, the write succeeded when it shouldn't have
                _lastException = null;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("metadata"))
            {
                // Debug: "Expected exception caught: {Message}", ex.Message);
                _lastException = ex;
            }
            catch (Exception ex)
            {
                // Debug: "Exception caught: {Message}", ex.Message);
                _lastException = ex;
            }
        }

        [Then(@"the '(.*)' process verifies the second metadata write should fail")]
        public void ThenProcessVerifiesTheSecondMetadataWriteShouldFail(string process)
        {
            // Debug: "Verifying second metadata write failed");
            
            Assert.NotNull(_lastException);
            Assert.True(_lastException is InvalidOperationException && 
                       (_lastException.Message.Contains("metadata") || _lastException.Message.Contains("Metadata")),
                       $"Expected metadata already written error but got: {_lastException?.Message}");
        }

        [Then(@"the '(.*)' process verifies the original metadata should remain unchanged")]
        public void ThenProcessVerifiesTheOriginalMetadataShouldRemainUnchanged(string process)
        {
            // Debug: "Verifying original metadata remains unchanged");
            
            // Get the reader for this buffer
            var reader = _basicSteps._readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found for buffer");
            }
            
            // Read current metadata and verify it wasn't changed by the second write attempt
            var currentMetadata = reader.GetMetadata();
            
            // The metadata should be size 500 (from the first write) not 200 (from the attempt)
            Assert.Equal(500, currentMetadata.Length);
        }

        [When(@"the '(.*)' process attempts to write metadata with size '(.*)'")]
        public void WhenProcessAttemptsToWriteMetadataWithSize(string process, string size)
        {
            // Debug: "Attempting to write metadata with size {Size}", size);
            
            try
            {
                // Get the writer for this buffer
                var writer = _basicSteps._writers.Values.LastOrDefault();
                if (writer == null)
                {
                    throw new InvalidOperationException($"No writer found for process '{process}'");
                }
                
                var metadataSize = int.Parse(size);
                var metadata = new byte[metadataSize];
                
                // Fill with test data
                for (int i = 0; i < metadataSize; i++)
                {
                    metadata[i] = (byte)(i % 256);
                }
                
                writer.SetMetadata(metadata);
                _lastException = null;
            }
            catch (Exception ex)
            {
                // Debug: "Exception caught: {Message}", ex.Message);
                _lastException = ex;
            }
        }

        [Then(@"the '(.*)' process verifies the metadata write should fail with size error")]
        public void ThenProcessVerifiesTheMetadataWriteShouldFailWithSizeError(string process)
        {
            // Debug: "Verifying metadata write failed with size error");
            
            Assert.NotNull(_lastException);
            Assert.True(_lastException is ArgumentException ||
                       _lastException is InvalidOperationException ||
                       _lastException.Message.Contains("size") || 
                       _lastException.Message.Contains("metadata") ||
                       _lastException.Message.Contains("exceed"),
                       $"Expected metadata size error but got: {_lastException?.Message}");
        }

        [When(@"the '(.*)' process writes frames without metadata")]
        public void WhenProcessWritesFramesWithoutMetadata(string process)
        {
            // Debug: "Writing frames without metadata");
            
            // Get the writer for this buffer
            var writer = _basicSteps._writers.Values.LastOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException($"No writer found for process '{process}'");
            }
            
            // Write several frames without metadata
            for (int i = 0; i < 3; i++)
            {
                var data = System.Text.Encoding.UTF8.GetBytes($"frame_{i}");
                writer.WriteFrame(data);
            }
        }

        [Then(@"the '(.*)' process should read frames successfully")]
        public void ThenProcessShouldReadFramesSuccessfully(string process)
        {
            // Debug: "Verifying frames can be read successfully");
            
            // Get the reader for this buffer
            var reader = _basicSteps._readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found for process '{process}'");
            }
            
            // Read the frames
            for (int i = 0; i < 3; i++)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(1));
                Assert.True(frame.IsValid, $"Frame {i} should be valid");
                
                // Convert ReadOnlySpan<byte> to byte array
                var data = new byte[frame.Span.Length];
                frame.Span.CopyTo(data);
                var text = System.Text.Encoding.UTF8.GetString(data);
                Assert.Equal($"frame_{i}", text);
            }
        }

        [Then(@"the '(.*)' process verifies the system should work without metadata")]
        public void ThenProcessVerifiesTheSystemShouldWorkWithoutMetadata(string process)
        {
            // Debug: "Verifying system works without metadata");
            
            // Get the reader for this buffer
            var reader = _basicSteps._readers.Values.LastOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException($"No reader found for buffer");
            }
            
            // Additional verification that the system works without metadata
            var metadata = reader.GetMetadata();
            
            // GetMetadata returns ReadOnlySpan<byte>, check if it's empty
            Assert.Equal(0, metadata.Length);
        }
    }
}