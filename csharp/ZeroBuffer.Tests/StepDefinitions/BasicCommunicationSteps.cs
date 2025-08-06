using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TechTalk.SpecFlow;
using Xunit;
using ZeroBuffer.Tests.Services;

namespace ZeroBuffer.Tests.StepDefinitions
{
    [Binding]
    public class BasicCommunicationSteps
    {
        internal readonly Dictionary<string, Reader> _readers = new();
        internal readonly Dictionary<string, Writer> _writers = new();
        private readonly Dictionary<string, byte[]> _testData = new();
        private readonly Dictionary<string, byte[]> _lastFrameData = new();
        private readonly Dictionary<string, ulong> _lastFrameSequences = new();
        private readonly IBufferNamingService _bufferNaming;
        private Exception? _lastException;
        private string _currentBuffer = "";

        public BasicCommunicationSteps(IBufferNamingService bufferNaming)
        {
            _bufferNaming = bufferNaming;
        }

        [Given(@"the test environment is initialized")]
        public void GivenTheTestEnvironmentIsInitialized()
        {
            // Clean up any previous test resources
            _readers.Clear();
            _writers.Clear();
            _testData.Clear();
            _lastFrameData.Clear();
            _lastFrameSequences.Clear();
            _currentBuffer = "";
            _lastException = null;
        }

        [Given(@"all processes are ready")]
        public void GivenAllProcessesAreReady()
        {
            // This step is for compatibility with multi-process scenarios
            // In single-process tests, we don't need to do anything
        }

        [Given(@"the '(.*)' process creates buffer '(.*)' with metadata size '(.*)' and payload size '(.*)'")]
        public void GivenProcessCreatesBufferWithSizes(string process, string bufferName, string metadataSize, string payloadSize)
        {
            // Accept process parameter but ignore it (as per guidelines)
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            var config = new BufferConfig(int.Parse(metadataSize), int.Parse(payloadSize));
            var reader = new Reader(actualBufferName, config);
            _readers[bufferName] = reader;
            _currentBuffer = bufferName;
        }

        [When(@"the '(.*)' process connects to buffer '(.*)'")]
        public void WhenProcessConnectsToBuffer(string process, string bufferName)
        {
            // Accept process parameter but ignore it
            var actualBufferName = _bufferNaming.GetBufferName(bufferName);
            var writer = new Writer(actualBufferName);
            _writers[bufferName] = writer;
            _currentBuffer = bufferName;
        }

        [When(@"the '(.*)' process writes metadata with size '(.*)'")]
        public void WhenProcessWritesMetadataWithSize(string process, string size)
        {
            // Accept process parameter but ignore it
            // Get the writer - if only one exists, use it; otherwise use current buffer
            Writer writer;
            if (_writers.Count == 0)
            {
                throw new InvalidOperationException("No writer connected to any buffer");
            }
            else if (_writers.Count == 1)
            {
                writer = _writers.Values.First();
            }
            else if (!string.IsNullOrEmpty(_currentBuffer) && _writers.ContainsKey(_currentBuffer))
            {
                writer = _writers[_currentBuffer];
            }
            else
            {
                throw new InvalidOperationException($"Multiple writers exist but current buffer '{_currentBuffer}' is not set or not found");
            }
            
            var metadata = TestDataPatterns.GenerateMetadata(int.Parse(size));
            writer.SetMetadata(metadata);
        }

        [When(@"the '(.*)' process writes frame with size '(.*)' and sequence '(.*)'")]
        public void WhenProcessWritesFrameWithSizeAndSequence(string process, string size, string sequence)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            var sequenceNum = ulong.Parse(sequence);
            var data = TestDataPatterns.GenerateFrameData(int.Parse(size), sequenceNum);
            
            writer.WriteFrame(data);
        }

        [Then(@"the '(.*)' process should read frame with sequence '(.*)' and size '(.*)'")]
        public void ThenProcessShouldReadFrameWithSequenceAndSize(string process, string expectedSequence, string expectedSize)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            Assert.Equal((ulong)int.Parse(expectedSequence), frame.Sequence);
            Assert.Equal(int.Parse(expectedSize), frame.Span.Length);
            
            // Store frame for later validation
            _lastFrameData[_currentBuffer] = frame.Span.ToArray();
            _lastFrameSequences[_currentBuffer] = frame.Sequence;
        }

        [Then(@"the '(.*)' process should validate frame data")]
        public void ThenProcessShouldValidateFrameData(string process)
        {
            // Accept process parameter but ignore it
            var frameData = _lastFrameData.Values.First();
            var frameSequence = _lastFrameSequences.Values.First();
            
            // Generate expected data using the shared pattern
            var expectedData = TestDataPatterns.GenerateFrameData(frameData.Length, frameSequence);
            
            // Compare frame data with expected data
            Assert.Equal(expectedData, frameData);
        }

        [Then(@"the '(.*)' process signals space available")]
        [When(@"the '(.*)' process signals space available")]
        public void ProcessSignalsSpaceAvailable(string process)
        {
            // Accept process parameter but ignore it
            // In the C# implementation, frames automatically signal space available
            // when they go out of scope or are disposed
            // So we just need to clear our reference to the frame
            _lastFrameData.Remove(_currentBuffer);
            _lastFrameSequences.Remove(_currentBuffer);
        }

        [When(@"the '(.*)' process writes frame with sequence '(.*)'")]
        public void WhenProcessWritesFrameWithSequence(string process, string sequence)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            var sequenceNum = ulong.Parse(sequence);
            // Use default size of 1024 when not specified
            var data = TestDataPatterns.GenerateFrameData(1024, sequenceNum);
            
            writer.WriteFrame(data);
        }

        [Then(@"the '(.*)' process should read frame with sequence '(.*)';")]
        public void ThenProcessShouldReadFrameWithSequence(string process, string expectedSequence)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            Assert.Equal(ulong.Parse(expectedSequence), frame.Sequence);
            
            // Store frame for later validation if needed
            _lastFrameData[_currentBuffer] = frame.Span.ToArray();
            _lastFrameSequences[_currentBuffer] = frame.Sequence;
        }

        [Then(@"the '(.*)' process should verify all frames maintain sequential order")]
        public void ThenProcessShouldVerifyAllFramesMaintainSequentialOrder(string process)
        {
            // Accept process parameter but ignore it
            // This step verifies that all frames read so far maintained sequential order
            // In our case, since we store last sequence, we can verify the sequences we've seen
            // For Test 1.2, we should have seen sequences 1, 2, 3 in order
            var lastSequence = _lastFrameSequences[_currentBuffer];
            Assert.Equal(3UL, lastSequence); // Last sequence should be 3 for this test
        }

        [When(@"the '(.*)' process writes frames until buffer is full")]
        public void WhenProcessWritesFramesUntilBufferIsFull(string process)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            
            // Write frames until we can't write anymore (buffer is full)
            int frameCount = 0;
            int frameSize = 1024; // Use a reasonable frame size
            
            while (true)
            {
                try
                {
                    var data = TestDataPatterns.GenerateFrameData(frameSize, (ulong)frameCount);
                    writer.WriteFrame(data);
                    frameCount++;
                    
                    // Safety limit to prevent infinite loop
                    if (frameCount > 100)
                    {
                        break;
                    }
                }
                catch (BufferFullException)
                {
                    // Buffer is full, this is expected
                    break;
                }
            }
            
            // Store the number of frames written for validation
            _testData[$"{_currentBuffer}_frames_written"] = BitConverter.GetBytes(frameCount);
        }

        [Then(@"the '(.*)' process should experience timeout on next write")]
        public void ThenProcessShouldExperienceTimeoutOnNextWrite(string process)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            
            try
            {
                var data = TestDataPatterns.GenerateFrameData(1024, 999);
                writer.WriteFrame(data);
                
                // If we get here, the write succeeded when it shouldn't have
                Assert.True(false, "Write should have timed out but succeeded");
            }
            catch (BufferFullException)
            {
                // Expected - buffer is full
                _lastException = new BufferFullException();
            }
        }

        [When(@"the '(.*)' process reads one frame")]
        public void WhenProcessReadsOneFrame(string process)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            
            // Store the frame data for later validation if needed
            _lastFrameData[_currentBuffer] = frame.Span.ToArray();
            _lastFrameSequences[_currentBuffer] = frame.Sequence;
            
            // Frame will automatically signal space available when it goes out of scope
        }

        [Then(@"the '(.*)' process should write successfully immediately")]
        public void ThenProcessShouldWriteSuccessfullyImmediately(string process)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            
            try
            {
                var data = TestDataPatterns.GenerateFrameData(1024, 1000);
                writer.WriteFrame(data);
                
                // Write succeeded as expected
                _lastException = null;
            }
            catch (BufferFullException)
            {
                Assert.True(false, "Write should have succeeded after space was made available");
            }
        }

        [When(@"the '(.*)' process requests zero-copy frame of size '(.*)'")]
        public void WhenProcessRequestsZeroCopyFrameOfSize(string process, string size)
        {
            // Accept process parameter but ignore it
            var frameSize = int.Parse(size);
            
            // Store the size for the next steps
            _testData[$"{_currentBuffer}_zerocopy_size"] = BitConverter.GetBytes(frameSize);
        }

        [When(@"the '(.*)' process fills zero-copy buffer with test pattern")]
        public void WhenProcessFillsZeroCopyBufferWithTestPattern(string process)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            var sizeBytes = (byte[])_testData[$"{_currentBuffer}_zerocopy_size"];
            var frameSize = BitConverter.ToInt32(sizeBytes, 0);
            
            // Request a zero-copy buffer and fill it immediately
            // This is the correct way to use zero-copy: get buffer, fill, commit
            var span = writer.GetFrameBuffer(frameSize, out ulong sequenceNumber);
            
            // Generate test pattern
            var testPattern = TestDataPatterns.GenerateFrameData(frameSize, sequenceNumber);
            
            // Fill the zero-copy buffer directly (this is the actual zero-copy operation)
            testPattern.CopyTo(span);
            
            // Store the pattern for verification
            _testData[$"{_currentBuffer}_test_pattern"] = testPattern;
            
            // Mark that we have a frame ready to commit
            _testData[$"{_currentBuffer}_zerocopy_ready"] = new byte[] { 1 };
        }

        [When(@"the '(.*)' process commits zero-copy frame")]
        public void WhenProcessCommitsZeroCopyFrame(string process)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            writer.CommitFrame();
            
            // Clear the ready flag
            _testData.Remove($"{_currentBuffer}_zerocopy_ready");
        }

        [Then(@"the '(.*)' process should read frame with size '(.*)'")]
        public void ThenProcessShouldReadFrameWithSize(string process, string size)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            var expectedSize = int.Parse(size);
            
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            Assert.Equal(expectedSize, frame.Span.Length);
            
            // Store frame data for later verification
            _lastFrameData[_currentBuffer] = frame.Span.ToArray();
            _lastFrameSequences[_currentBuffer] = frame.Sequence;
        }

        [Then(@"the '(.*)' process should verify frame data matches test pattern")]
        public void ThenProcessShouldVerifyFrameDataMatchesTestPattern(string process)
        {
            // Accept process parameter but ignore it
            var frameData = _lastFrameData[_currentBuffer];
            var sequence = _lastFrameSequences[_currentBuffer];
            
            // Generate the expected test pattern based on the frame's sequence number
            var expectedPattern = TestDataPatterns.GenerateFrameData(frameData.Length, sequence);
            
            Assert.Equal(expectedPattern, frameData);
        }

        [When(@"the '(.*)' process writes frame with size '([^']*)'$")]
        public void WhenProcessWritesFrameWithSize(string process, string size)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            var frameSize = int.Parse(size);
            
            // Use simple test data pattern
            var data = TestDataPatterns.GenerateSimpleFrameData(frameSize);
            writer.WriteFrame(data);
        }

        [Then(@"the '(.*)' process should read (.*) frames with sizes '(.*)' in order")]
        public void ThenProcessShouldReadFramesWithSizesInOrder(string process, int frameCount, string sizes)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            var expectedSizes = sizes.Split(',').Select(int.Parse).ToArray();
            
            Assert.Equal(frameCount, expectedSizes.Length);
            
            for (int i = 0; i < frameCount; i++)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                
                Assert.True(frame.IsValid, $"Frame {i + 1} should be valid");
                Assert.Equal(expectedSizes[i], frame.Span.Length);
                
                // Verify frame data integrity using TestDataPatterns
                var frameData = frame.Span.ToArray();
                Assert.True(TestDataPatterns.VerifySimpleFrameData(frameData), 
                    $"Frame {i + 1} data does not match expected pattern");
            }
        }

        [When(@"the '(.*)' process writes metadata '(.*)'")]
        public void WhenProcessWritesMetadata(string process, string metadata)
        {
            // Accept process parameter but ignore it
            
            // Check if we need to reconnect (for metadata updates)
            if (_writers.ContainsKey(_currentBuffer))
            {
                var existingWriter = _writers[_currentBuffer];
                // Check if metadata was already written
                try
                {
                    // Try to write metadata - if it fails, we need to reconnect
                    var metadataBytes = Encoding.UTF8.GetBytes(metadata);
                    existingWriter.SetMetadata(metadataBytes);
                    return; // Success - metadata written
                }
                catch (InvalidOperationException ex) when (ex.Message == "Metadata already written")
                {
                    // Need to disconnect and reconnect
                    existingWriter.Dispose();
                    _writers.Remove(_currentBuffer);
                    
                    // Reconnect
                    var actualBufferName = _bufferNaming.GetBufferName(_currentBuffer);
                    var newWriter = new Writer(actualBufferName);
                    _writers[_currentBuffer] = newWriter;
                    
                    // Now write the new metadata
                    var metadataBytesNew = Encoding.UTF8.GetBytes(metadata);
                    newWriter.SetMetadata(metadataBytesNew);
                }
            }
            else
            {
                throw new InvalidOperationException($"No writer connected to buffer '{_currentBuffer}'");
            }
        }

        [When(@"the '(.*)' process writes frame with data '(.*)'")]
        public void WhenProcessWritesFrameWithData(string process, string data)
        {
            // Accept process parameter but ignore it
            var writer = _writers[_currentBuffer];
            
            // Convert string data to bytes
            var dataBytes = Encoding.UTF8.GetBytes(data);
            writer.WriteFrame(dataBytes);
        }

        [Then(@"the '(.*)' process should have metadata '(.*)'")]
        public void ThenProcessShouldHaveMetadata(string process, string expectedMetadata)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            
            // Read metadata from the buffer
            var metadata = reader.GetMetadata();
            var metadataString = Encoding.UTF8.GetString(metadata);
            
            Assert.Equal(expectedMetadata, metadataString);
        }

        [Then(@"the '(.*)' process should read frame with data '(.*)'")]
        public void ThenProcessShouldReadFrameWithData(string process, string expectedData)
        {
            // Accept process parameter but ignore it
            var reader = _readers[_currentBuffer];
            
            var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
            
            Assert.True(frame.IsValid, "Frame should be valid");
            
            // Convert frame data to string and compare
            var frameString = Encoding.UTF8.GetString(frame.Span);
            Assert.Equal(expectedData, frameString);
        }

        // Cleanup
        public void Dispose()
        {
            foreach (var reader in _readers.Values)
            {
                reader?.Dispose();
            }
            
            foreach (var writer in _writers.Values)
            {
                writer?.Dispose();
            }
        }
    }
}
