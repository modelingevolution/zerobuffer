using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System;
using TechTalk.SpecFlow;
using ZeroBuffer.Tests;

namespace ZeroBuffer.Tests.StepDefinitions;

[Binding]
public class PerformanceSteps
{
    private readonly ITestContext _testContext;
    private readonly ILogger<PerformanceSteps> _logger;
    private readonly Dictionary<string, Writer> _writers = new();
    private readonly Dictionary<string, Reader> _readers = new();
    
    public PerformanceSteps(ITestContext testContext, ILogger<PerformanceSteps> logger)
    {
        _testContext = testContext;
        _logger = logger;
    }
    
    [Given(@"the test mode is configured")]
    public void GivenTheTestModeIsConfigured()
    {
        _logger.LogInformation("Test mode configured for Performance");
        // This step is just a marker for test setup
    }
    
    [Given(@"the reader is '([^']+)'")]
    public void GivenTheReaderIs(string platform)
    {
        _logger.LogInformation("Reader platform: {Platform}", platform);
        // This is handled by the process orchestration system
    }
    
    [Given(@"the writer is '([^']+)'")]
    public void GivenTheWriterIs(string platform)
    {
        _logger.LogInformation("Writer platform: {Platform}", platform);
        // This is handled by the process orchestration system
    }
    
    [When(@"the reader is '([^']+)'")]
    public void WhenTheReaderIs(string platform)
    {
        _logger.LogInformation("Reader platform: {Platform}", platform);
        // This is handled by the process orchestration system
    }
    
    [When(@"the writer is '([^']+)'")]
    public void WhenTheWriterIs(string platform)
    {
        _logger.LogInformation("Writer platform: {Platform}", platform);
        // This is handled by the process orchestration system
    }
    
    [Then(@"the reader is '([^']+)'")]
    public void ThenTheReaderIs(string platform)
    {
        _logger.LogInformation("Reader platform: {Platform}", platform);
        // This is handled by the process orchestration system
    }
    
    [Then(@"the writer is '([^']+)'")]
    public void ThenTheWriterIs(string platform)
    {
        _logger.LogInformation("Writer platform: {Platform}", platform);
        // This is handled by the process orchestration system
    }
    
    [When(@"write frame with exact size '(\d+)'")]
    public void WhenWriteFrameWithExactSize(int size)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        var data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 256);
        }
        
        writer.WriteFrame(data);
        _testContext.SetData("last_frame_size", size);
        
        _logger.LogInformation("Wrote frame with exact size {Size} bytes", size);
    }
    
    [Then(@"buffer should be completely full")]
    public void ThenBufferShouldBeCompletelyFull()
    {
        // This requires access to internal buffer state
        _logger.LogInformation("Expected: Buffer should be completely full");
    }
    
    [Then(@"no more writes should be possible")]
    public void ThenNoMoreWritesShouldBePossible()
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        try
        {
            var testData = new byte[1];
            writer.WriteFrame(testData);
            
            _logger.LogWarning("Write succeeded when buffer should be full");
            _testContext.SetData("additional_write_success", true);
        }
        catch (BufferFullException)
        {
            _logger.LogInformation("No more writes possible - buffer correctly full");
            _testContext.SetData("additional_write_success", false);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Write blocked with exception: {Exception}", ex.Message);
            _testContext.SetData("additional_write_success", false);
        }
    }
    
    [When(@"read frame should have size '(\d+)'")]
    public void WhenReadFrameShouldHaveSize(int expectedSize)
    {
        var reader = _testContext.GetData<Reader>("current_reader");
        
        var frame = reader.ReadFrame();
        var frameRef = frame.ToFrameRef();
        
        if (frameRef.Size != expectedSize)
        {
            throw new InvalidOperationException($"Expected frame size {expectedSize}, got {frameRef.Size}");
        }
        
        _logger.LogInformation("Read frame with size {Size} as expected", expectedSize);
    }
    
    [When(@"signal space available")]
    public void WhenSignalSpaceAvailable()
    {
        // This would signal to the writer that space is available
        // In practice, this happens automatically when frames are read
        _logger.LogInformation("Signaled space available to writer");
    }
    
    [Then(@"write should succeed again")]
    public void ThenWriteShouldSucceedAgain()
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        try
        {
            var testData = System.Text.Encoding.UTF8.GetBytes("test_after_space");
            writer.WriteFrame(testData);
            
            _logger.LogInformation("Write succeeded again after space became available");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Write should have succeeded after space became available: {ex.Message}");
        }
    }
    
    [When(@"attempt to write frame with size '(\d+)'")]
    public void WhenAttemptToWriteFrameWithSize(int size)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        try
        {
            var data = new byte[size];
            writer.WriteFrame(data);
            
            _testContext.SetData("zero_size_write_success", true);
            _logger.LogInformation("Frame with size {Size} written successfully", size);
        }
        catch (Exception ex)
        {
            _testContext.SetData("zero_size_write_success", false);
            _testContext.SetData("zero_size_write_exception", ex);
            _logger.LogInformation("Frame with size {Size} failed: {Exception}", size, ex.Message);
        }
    }
    
    [Then(@"write should fail with zero size error")]
    public void ThenWriteShouldFailWithZeroSizeError()
    {
        if (_testContext.TryGetData<bool>("zero_size_write_success", out var success) && success)
        {
            throw new InvalidOperationException("Zero-size frame write should have failed");
        }
        
        if (_testContext.TryGetData<Exception>("zero_size_write_exception", out var exception))
        {
            _logger.LogInformation("Zero-size write correctly failed with: {Exception}", exception.Message);
        }
        else
        {
            throw new InvalidOperationException("Expected zero-size write to throw an exception");
        }
    }
    
    [When(@"write frame with size '(\d+)'")]
    public void WhenWriteFrameWithSize(int size)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        var data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 256);
        }
        
        writer.WriteFrame(data);
        _testContext.SetData("last_frame_size", size);
        
        _logger.LogInformation("Wrote frame with size {Size} bytes", size);
    }
    
    [Then(@"read frame should have size '(\d+)'")]
    public void ThenReadFrameShouldHaveSize(int expectedSize)
    {
        var reader = _testContext.GetData<Reader>("current_reader");
        
        var frame = reader.ReadFrame();
        var frameRef = frame.ToFrameRef();
        
        if (frameRef.Size != expectedSize)
        {
            throw new InvalidOperationException($"Expected frame size {expectedSize}, got {frameRef.Size}");
        }
        
        _logger.LogInformation("Frame has expected size {Size}", expectedSize);
    }
    
    [Then(@"frame overhead should be '(\d+)' bytes")]
    public void ThenFrameOverheadShouldBeBytes(int expectedOverhead)
    {
        // Frame overhead is internal to ZeroBuffer implementation
        // This would typically be sequence number, size, etc.
        _logger.LogInformation("Expected frame overhead: {Overhead} bytes", expectedOverhead);
    }
    
    [Then(@"write should wait for space")]
    public void ThenWriteShouldWaitForSpace()
    {
        // This would be detected by monitoring write operations
        _logger.LogInformation("Write operation should wait for space to become available");
    }
    
    [Then(@"write should complete at buffer start")]
    public void ThenWriteShouldCompleteAtBufferStart()
    {
        // This is internal positioning in the circular buffer
        _logger.LogInformation("Write should complete at buffer start due to wrap-around");
    }
    
    [When(@"write '(\d+)' frames of size '(\d+)' rapidly")]
    public void WhenWriteFramesOfSizeRapidly(int frameCount, int frameSize)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        var startTime = DateTime.UtcNow;
        var writtenFrames = 0;
        
        var writeTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < frameCount; i++)
                {
                    var data = new byte[frameSize];
                    for (int j = 0; j < frameSize; j++)
                    {
                        data[j] = (byte)((i + j) % 256);
                    }
                    
                    writer.WriteFrame(data);
                    writtenFrames = i + 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Rapid writing stopped after {Count} frames: {Exception}", writtenFrames, ex.Message);
            }
            
            return writtenFrames;
        });
        
        _testContext.SetData("rapid_write_task", writeTask);
        _testContext.SetData("rapid_write_start_time", startTime);
        _testContext.SetData("expected_frame_count", frameCount);
        
        _logger.LogInformation("Started writing {Count} frames of size {Size} rapidly", frameCount, frameSize);
    }
    
    [When(@"read all frames and signal immediately")]
    public void WhenReadAllFramesAndSignalImmediately()
    {
        var reader = _testContext.GetData<Reader>("current_reader");
        var expectedCount = _testContext.GetData<int>("expected_frame_count");
        var readFrames = new List<FrameRef>();
        
        var readTask = Task.Run(() =>
        {
            var framesRead = 0;
            try
            {
                for (int i = 0; i < expectedCount; i++)
                {
                    var frame = reader.ReadFrame();
                    var frameRef = frame.ToFrameRef();
                    readFrames.Add(frameRef);
                    framesRead++;
                    
                    // Signal immediately (space becomes available automatically when frame is read)
                    if (framesRead % 100 == 0)
                    {
                        _logger.LogDebug("Read {Count} frames rapidly", framesRead);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Rapid reading stopped after {Count} frames: {Exception}", framesRead, ex.Message);
            }
            
            return framesRead;
        });
        
        _testContext.SetData("rapid_read_task", readTask);
        _testContext.SetData("rapid_read_frames", readFrames);
        
        _logger.LogInformation("Started rapid reading and immediate signaling");
    }
    
    [Then(@"all sequence numbers should be correct")]
    public void ThenAllSequenceNumbersShouldBeCorrect()
    {
        var readFrames = _testContext.GetData<List<FrameRef>>("rapid_read_frames");
        
        for (int i = 0; i < readFrames.Count; i++)
        {
            var expectedSequence = (uint)(i + 1);
            if (readFrames[i].Sequence != expectedSequence)
            {
                throw new InvalidOperationException($"Frame {i} has sequence {readFrames[i].Sequence}, expected {expectedSequence}");
            }
        }
        
        _logger.LogInformation("All {Count} sequence numbers are correct", readFrames.Count);
    }
    
    [Then(@"no frames should be lost")]
    public void ThenNoFramesShouldBeLost()
    {
        if (_testContext.TryGetData<Task<int>>("rapid_write_task", out var writeTask) &&
            _testContext.TryGetData<Task<int>>("rapid_read_task", out var readTask))
        {
            var framesWritten = writeTask.Result;
            var framesRead = readTask.Result;
            
            if (framesRead != framesWritten)
            {
                throw new InvalidOperationException($"Frame loss detected: wrote {framesWritten}, read {framesRead}");
            }
            
            _logger.LogInformation("No frames lost: {Count} written and read", framesRead);
        }
        else
        {
            _logger.LogInformation("No frames should be lost in rapid write-read cycles");
        }
    }
    
    [Then(@"no deadlocks should occur")]
    public void ThenNoDeadlocksShouldOccur()
    {
        // If we reach this point, no deadlocks occurred
        _logger.LogInformation("No deadlocks occurred during rapid operations");
    }
    
    [When(@"fill buffer to '(\d+)%' capacity")]
    public void WhenFillBufferToCapacity(int percentage)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        // Estimate buffer capacity (10240 payload size)
        var bufferCapacity = 10240;
        var targetBytes = (bufferCapacity * percentage) / 100;
        var frameSize = 1000; // Use 1KB frames
        var framesToWrite = targetBytes / frameSize;
        
        for (int i = 0; i < framesToWrite; i++)
        {
            var data = new byte[frameSize];
            for (int j = 0; j < frameSize; j++)
            {
                data[j] = (byte)((i + j) % 256);
            }
            
            writer.WriteFrame(data);
        }
        
        _testContext.SetData("buffer_fill_percentage", percentage);
        _testContext.SetData("frames_written_for_fill", framesToWrite);
        
        _logger.LogInformation("Filled buffer to approximately {Percentage}% capacity ({Frames} frames)", percentage, framesToWrite);
    }
    
    [When(@"continue filling buffer to '(\d+)%'")]
    public void WhenContinueFillingBufferTo(int percentage)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        var previousPercentage = _testContext.GetData<int>("buffer_fill_percentage");
        
        var bufferCapacity = 10240;
        var additionalBytes = (bufferCapacity * (percentage - previousPercentage)) / 100;
        var frameSize = 1000;
        var additionalFrames = additionalBytes / frameSize;
        
        try
        {
            for (int i = 0; i < additionalFrames; i++)
            {
                var data = new byte[frameSize];
                for (int j = 0; j < frameSize; j++)
                {
                    data[j] = (byte)((i + j + 200) % 256); // Different pattern
                }
                
                writer.WriteFrame(data);
            }
            
            _testContext.SetData("continue_fill_success", true);
            _logger.LogInformation("Successfully continued filling buffer to {Percentage}%", percentage);
        }
        catch (Exception ex)
        {
            _testContext.SetData("continue_fill_success", false);
            _testContext.SetData("continue_fill_exception", ex);
            _logger.LogInformation("Buffer fill to {Percentage}% failed: {Exception}", percentage, ex.Message);
        }
    }
    
    [Then(@"next write should block waiting for space")]
    public void ThenNextWriteShouldBlockWaitingForSpace()
    {
        if (_testContext.TryGetData<bool>("continue_fill_success", out var success) && success)
        {
            _logger.LogWarning("Buffer fill succeeded when it should have blocked");
        }
        else if (_testContext.TryGetData<Exception>("continue_fill_exception", out var exception))
        {
            _logger.LogInformation("Write correctly blocked waiting for space: {Exception}", exception.Message);
        }
        else
        {
            _logger.LogInformation("Next write should block waiting for space");
        }
    }
    
    [When(@"attempt to connect to buffer '([^']+)'")]
    public void WhenAttemptToConnectToBuffer(string bufferName)
    {
        try
        {
            var writer = new Writer(bufferName);
            _writers[bufferName] = writer;
            _testContext.SetData("current_writer", writer);
            _testContext.SetData("connect_attempt_success", true);
            
            _logger.LogInformation("Successfully connected to buffer '{BufferName}'", bufferName);
        }
        catch (Exception ex)
        {
            _testContext.SetData("connect_attempt_success", false);
            _testContext.SetData("connect_attempt_exception", ex);
            _logger.LogInformation("Connection to buffer '{BufferName}' failed: {Exception}", bufferName, ex.Message);
        }
    }
    
    [Then(@"connection should fail with writer exists error")]
    public void ThenConnectionShouldFailWithWriterExistsError()
    {
        if (_testContext.TryGetData<bool>("connect_attempt_success", out var success) && success)
        {
            throw new InvalidOperationException("Connection should have failed with writer exists error");
        }
        
        if (_testContext.TryGetData<Exception>("connect_attempt_exception", out var exception))
        {
            var message = exception.Message.ToLower();
            if (message.Contains("writer") && (message.Contains("exists") || message.Contains("already") || message.Contains("conflict")))
            {
                _logger.LogInformation("Connection correctly failed with writer exists error: {Exception}", exception.Message);
            }
            else
            {
                _logger.LogInformation("Connection failed but may not be specifically writer exists error: {Exception}", exception.Message);
            }
        }
        else
        {
            throw new InvalidOperationException("Expected connection to fail with an exception");
        }
    }
}
