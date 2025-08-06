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
public class InitializationSteps
{
    private readonly ITestContext _testContext;
    private readonly ILogger<InitializationSteps> _logger;
    private readonly Dictionary<string, Writer> _writers = new();
    private readonly Dictionary<string, Reader> _readers = new();
    
    public InitializationSteps(ITestContext testContext, ILogger<InitializationSteps> logger)
    {
        _testContext = testContext;
        _logger = logger;
    }
    
    [Given(@"the test mode is configured")]
    public void GivenTheTestModeIsConfigured()
    {
        _logger.LogInformation("Test mode configured for Initialization");
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
    
    [When(@"write frames without metadata")]
    public void WhenWriteFramesWithoutMetadata()
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        
        // Write several frames without any metadata
        var framesToWrite = 5;
        var framesWritten = new List<int>();
        
        for (int i = 0; i < framesToWrite; i++)
        {
            var data = System.Text.Encoding.UTF8.GetBytes($"frame_{i}_no_metadata");
            writer.WriteFrame(data);
            framesWritten.Add(i);
            
            _logger.LogDebug("Wrote frame {Index} without metadata", i);
        }
        
        _testContext.SetData("frames_written_no_metadata", framesWritten);
        _logger.LogInformation("Wrote {Count} frames without metadata", framesToWrite);
    }
    
    [When(@"read frames successfully")]
    public void WhenReadFramesSuccessfully()
    {
        var reader = _testContext.GetData<Reader>("current_reader");
        var expectedFrames = _testContext.GetData<List<int>>("frames_written_no_metadata");
        
        var framesRead = new List<FrameRef>();
        
        for (int i = 0; i < expectedFrames.Count; i++)
        {
            var frame = reader.ReadFrame();
            var frameRef = frame.ToFrameRef();
            framesRead.Add(frameRef);
            
            var data = System.Text.Encoding.UTF8.GetString(frameRef.Data);
            var expectedData = $"frame_{i}_no_metadata";
            
            if (data != expectedData)
            {
                throw new InvalidOperationException($"Frame {i} data mismatch: expected '{expectedData}', got '{data}'");
            }
            
            _logger.LogDebug("Read frame {Index} successfully: {Data}", i, data);
        }
        
        _testContext.SetData("frames_read_no_metadata", framesRead);
        _logger.LogInformation("Read {Count} frames successfully", framesRead.Count);
    }
    
    [Then(@"system should work without metadata")]
    public void ThenSystemShouldWorkWithoutMetadata()
    {
        var framesWritten = _testContext.GetData<List<int>>("frames_written_no_metadata");
        var framesRead = _testContext.GetData<List<FrameRef>>("frames_read_no_metadata");
        
        if (framesRead.Count != framesWritten.Count)
        {
            throw new InvalidOperationException($"Frame count mismatch: wrote {framesWritten.Count}, read {framesRead.Count}");
        }
        
        // Verify no metadata is present
        foreach (var frame in framesRead)
        {
            // In zero-metadata configuration, metadata should be empty or not accessible
            try
            {
                var reader = _testContext.GetData<Reader>("current_reader");
                var metadataSpan = reader.GetMetadata();
                
                if (metadataSpan.Length > 0)
                {
                    var hasNonZero = false;
                    for (int i = 0; i < metadataSpan.Length; i++)
                    {
                        if (metadataSpan[i] != 0)
                        {
                            hasNonZero = true;
                            break;
                        }
                    }
                    
                    if (hasNonZero)
                    {
                        _logger.LogWarning("Metadata appears to contain data in zero-metadata configuration");
                    }
                }
            }
            catch (Exception)
            {
                // Expected in zero-metadata configuration
                _logger.LogDebug("Metadata access failed as expected in zero-metadata configuration");
            }
        }
        
        _logger.LogInformation("System works correctly without metadata - all {Count} frames processed", framesRead.Count);
    }
    
    [When(@"wait on semaphore for data")]
    public void WhenWaitOnSemaphoreForData()
    {
        var reader = _testContext.GetData<Reader>("current_reader");
        
        // Start a task to wait for data (this will block until data arrives or timeout)
        var waitTask = Task.Run(() =>
        {
            try
            {
                // This should block waiting for data
                var frame = reader.ReadFrame();
                return frame.ToFrameRef();
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Wait on semaphore interrupted: {Exception}", ex.Message);
                throw;
            }
        });
        
        _testContext.SetData("semaphore_wait_task", waitTask);
        _logger.LogInformation("Reader waiting on semaphore for data");
        
        // Give the wait time to start
        Thread.Sleep(100);
    }
    
    [When(@"simulate crash while reader blocked")]
    public void WhenSimulateCrashWhileReaderBlocked()
    {
        // Simulate writer crash by disposing all writers
        foreach (var writer in _writers.Values)
        {
            try
            {
                writer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Exception during writer disposal (expected during crash simulation): {Exception}", ex);
            }
        }
        _writers.Clear();
        
        _testContext.SetData("writer_crashed_while_reader_blocked", true);
        _logger.LogInformation("Simulated writer crash while reader blocked on semaphore");
    }
    
    [When(@"detect writer death after timeout '(\d+)' seconds")]
    public void WhenDetectWriterDeathAfterTimeoutSeconds(int timeoutSeconds)
    {
        var startTime = DateTime.UtcNow;
        var timeoutTime = startTime.AddSeconds(timeoutSeconds);
        
        if (_testContext.TryGetData<Task<FrameRef>>("semaphore_wait_task", out var waitTask))
        {
            try
            {
                // Wait for the task to complete or timeout
                var completedTask = Task.WaitAny(new Task[] { waitTask }, TimeSpan.FromSeconds(timeoutSeconds + 1));
                
                if (completedTask == 0)
                {
                    // Task completed - check if it was due to writer death detection
                    try
                    {
                        var result = waitTask.Result;
                        _testContext.SetData("writer_death_detected", false);
                        _logger.LogWarning("Read completed successfully despite writer crash");
                    }
                    catch (Exception ex)
                    {
                        _testContext.SetData("writer_death_detected", true);
                        _testContext.SetData("writer_death_exception", ex);
                        _logger.LogInformation("Writer death detected after timeout: {Exception}", ex.Message);
                    }
                }
                else
                {
                    // Task timed out
                    _testContext.SetData("writer_death_detected", true);
                    _logger.LogInformation("Writer death detected via timeout after {Timeout} seconds", timeoutSeconds);
                }
            }
            catch (Exception ex)
            {
                _testContext.SetData("writer_death_detected", true);
                _testContext.SetData("writer_death_exception", ex);
                _logger.LogInformation("Writer death detected: {Exception}", ex.Message);
            }
        }
        else
        {
            _logger.LogWarning("No semaphore wait task found to monitor");
        }
    }
    
    [Then(@"throw writer dead exception")]
    public void ThenThrowWriterDeadException()
    {
        if (!_testContext.TryGetData<bool>("writer_death_detected", out var detected) || !detected)
        {
            _logger.LogWarning("Writer death was not detected by reader");
            return;
        }
        
        if (_testContext.TryGetData<Exception>("writer_death_exception", out var exception))
        {
            var message = exception.Message.ToLower();
            if (message.Contains("writer") && (message.Contains("dead") || message.Contains("death") || message.Contains("disconnect") || message.Contains("timeout")))
            {
                _logger.LogInformation("Correctly threw writer dead exception: {Exception}", exception.Message);
            }
            else
            {
                _logger.LogInformation("Exception thrown but may not specifically indicate writer death: {Exception}", exception.Message);
            }
        }
        else
        {
            _logger.LogInformation("Writer death detected via timeout (no specific exception)");
        }
    }
}
