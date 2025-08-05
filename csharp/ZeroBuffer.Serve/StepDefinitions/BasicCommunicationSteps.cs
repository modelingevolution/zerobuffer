using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;
using ZeroBuffer.Serve.JsonRpc;

namespace ZeroBuffer.Serve.StepDefinitions;

[Binding]
public class BasicCommunicationSteps
{
    private readonly ITestContext _testContext;
    private readonly ILogger<BasicCommunicationSteps> _logger;
    private readonly Dictionary<string, Writer> _writers = new();
    private readonly Dictionary<string, Reader> _readers = new();
    private FrameRef? _lastFrame;
    
    public BasicCommunicationSteps(ITestContext testContext, ILogger<BasicCommunicationSteps> logger)
    {
        _testContext = testContext;
        _logger = logger;
    }
    
    [Given(@"the test environment is initialized")]
    public void GivenTheTestEnvironmentIsInitialized()
    {
        _logger.LogInformation("Test environment initialized");
        // Any global initialization can happen here
    }
    
    [Given(@"all processes are ready")]
    public void GivenAllProcessesAreReady()
    {
        _logger.LogInformation("All processes ready");
        // This step is handled by Harmony orchestrator
    }
    
    [Given(@"creates buffer '([^']+)' with metadata size '(\d+)' and payload size '(\d+)'")]
    public void GivenCreatesBufferWithSizes(string bufferName, int metadataSize, int payloadSize)
    {
        _logger.LogInformation("Creating buffer '{BufferName}' with metadata size {MetadataSize} and payload size {PayloadSize}", 
            bufferName, metadataSize, payloadSize);
        
        var config = new BufferConfig
        {
            MetadataSize = metadataSize,
            PayloadSize = payloadSize
        };
        
        var reader = new Reader(bufferName, config);
        _readers[bufferName] = reader;
        _testContext.SetData($"buffer_{bufferName}", reader);
        _testContext.SetData("current_reader", reader);
    }
    
    [When(@"connects to buffer '([^']+)'")]
    public void WhenConnectsToBuffer(string bufferName)
    {
        _logger.LogInformation("Connecting to buffer '{BufferName}'", bufferName);
        
        var writer = new Writer(bufferName);
        _writers[bufferName] = writer;
        _testContext.SetData($"writer_{bufferName}", writer);
        _testContext.SetData("current_writer", writer);
    }
    
    [When(@"writes metadata with size '(\d+)'")]
    public void WhenWritesMetadataWithSize(int size)
    {
        // Try to get the writer from test context first
        Writer writer;
        try
        {
            writer = _testContext.GetData<Writer>("current_writer");
        }
        catch (KeyNotFoundException)
        {
            // Fallback to dictionary
            writer = _writers.Values.FirstOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException("No writer available");
            }
        }
        
        // Get metadata buffer for zero-copy write
        var metadataBuffer = writer.GetMetadataBuffer(size);
        
        // Fill with test pattern
        for (int i = 0; i < size; i++)
        {
            metadataBuffer[i] = (byte)(i % 256);
        }
        
        // Commit metadata
        writer.CommitMetadata();
        
        _logger.LogInformation("Wrote metadata with size {Size}", size);
    }
    
    [When(@"writes frame with size '(\d+)' and sequence '(\d+)'")]
    public void WhenWritesFrameWithSizeAndSequence(int size, uint sequence)
    {
        // Try to get the writer from test context first
        Writer writer;
        try
        {
            writer = _testContext.GetData<Writer>("current_writer");
        }
        catch (KeyNotFoundException)
        {
            // Fallback to dictionary
            writer = _writers.Values.FirstOrDefault();
            if (writer == null)
            {
                throw new InvalidOperationException("No writer available");
            }
        }
        
        var data = new byte[size];
        // Fill with test pattern based on sequence
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)((i + sequence) % 256);
        }
        
        writer.WriteFrame(data);
        _logger.LogInformation("Wrote frame with size {Size}", size);
    }
    
    [When(@"writes frame with sequence '(\d+)'")]
    public void WhenWritesFrameWithSequence(uint sequence)
    {
        // Default size of 1024 bytes
        WhenWritesFrameWithSizeAndSequence(1024, sequence);
    }
    
    [Then(@"should read frame with sequence '(\d+)' and size '(\d+)'")]
    public void ThenShouldReadFrameWithSequenceAndSize(uint expectedSequence, int expectedSize)
    {
        // Try to get the reader from test context first
        Reader reader;
        try
        {
            reader = _testContext.GetData<Reader>("current_reader");
        }
        catch (KeyNotFoundException)
        {
            // Fallback to dictionary
            reader = _readers.Values.FirstOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException("No reader available");
            }
        }
        
        var frame = reader.ReadFrame();
        
        var frameRef = frame.ToFrameRef();
        
        // Store in test context for validation
        _testContext.SetData("last_frame", frameRef);
        
        _lastFrame = frameRef;
        _logger.LogInformation("Read frame with sequence {Sequence}, size {Size}", 
            frame.Sequence, frame.Size);
        
        if (frame.Sequence != expectedSequence)
        {
            throw new InvalidOperationException($"Expected sequence {expectedSequence}, got {frame.Sequence}");
        }
        
        if (frame.Size != expectedSize)
        {
            throw new InvalidOperationException($"Expected size {expectedSize}, got {frame.Size}");
        }
    }
    
    [Then(@"should read frame with sequence '(\d+)'")]
    public void ThenShouldReadFrameWithSequence(uint expectedSequence)
    {
        // Try to get the reader from test context first
        Reader reader;
        try
        {
            reader = _testContext.GetData<Reader>("current_reader");
        }
        catch (KeyNotFoundException)
        {
            // Fallback to dictionary
            reader = _readers.Values.FirstOrDefault();
            if (reader == null)
            {
                throw new InvalidOperationException("No reader available");
            }
        }
        
        var frame = reader.ReadFrame();
        
        var frameRef = frame.ToFrameRef();
        
        // Store in test context for validation
        _testContext.SetData("last_frame", frameRef);
        
        _lastFrame = frameRef;
        _logger.LogInformation("Read frame with sequence {Sequence}", frame.Sequence);
        
        if (frame.Sequence != expectedSequence)
        {
            throw new InvalidOperationException($"Expected sequence {expectedSequence}, got {frame.Sequence}");
        }
    }
    
    [Then(@"should validate frame data")]
    public void ThenShouldValidateFrameData()
    {
        // Try to get from test context first
        FrameRef frameRef;
        
        try
        {
            frameRef = _testContext.GetData<FrameRef>("last_frame");
        }
        catch (KeyNotFoundException)
        {
            if (_lastFrame == null)
            {
                throw new InvalidOperationException("No frame to validate");
            }
            frameRef = _lastFrame;
        }
        
        var data = frameRef.Data;
        var sequence = frameRef.Sequence;
        
        // Validate test pattern
        for (int i = 0; i < data.Length; i++)
        {
            var expected = (byte)((i + (int)sequence) % 256);
            if (data[i] != expected)
            {
                throw new InvalidOperationException($"Data validation failed at position {i}: expected {expected}, got {data[i]}");
            }
        }
        
        _logger.LogInformation("Frame data validated successfully");
    }
    
    [Then(@"signals space available")]
    [When(@"signals space available")]
    [Given(@"signals space available")]
    public void SignalsSpaceAvailable()
    {
        // In the current ZeroBuffer implementation, space is automatically managed
        // The reader automatically signals space when frames are read
        _logger.LogInformation("Space availability is automatically managed by ZeroBuffer");
    }
}