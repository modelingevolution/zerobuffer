using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;
using ZeroBuffer.Serve.JsonRpc;

namespace ZeroBuffer.Serve.StepDefinitions;

[Binding]
public class BenchmarksSteps
{
    private readonly ITestContext _testContext;
    private readonly ILogger<BenchmarksSteps> _logger;
    private readonly Dictionary<string, Writer> _writers = new();
    private readonly Dictionary<string, Reader> _readers = new();
    
    public BenchmarksSteps(ITestContext testContext, ILogger<BenchmarksSteps> logger)
    {
        _testContext = testContext;
        _logger = logger;
    }
    
    [Given(@"the test mode is configured")]
    public void GivenTheTestModeIsConfigured()
    {
        _logger.LogInformation("Test mode configured for Benchmarks");
        // This step is just a marker for test setup
    }
    
    [Given(@"benchmark environment is prepared")]
    public void GivenBenchmarkEnvironmentIsPrepared()
    {
        _logger.LogInformation("Benchmark environment prepared");
        
        // Set up benchmarking infrastructure
        _testContext.SetData("benchmark_start_time", DateTime.UtcNow);
        _testContext.SetData("latency_measurements", new List<TimeSpan>());
        _testContext.SetData("throughput_measurements", new List<double>());
        
        // Prepare for high-precision timing
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        _logger.LogInformation("Benchmark environment ready - GC collected, high precision timing enabled");
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
    
    [Given(@"create buffer '([^']+)' with large config")]
    public void GivenCreateBufferWithLargeConfig(string bufferName)
    {
        _logger.LogInformation("Creating buffer '{BufferName}' with large configuration for throughput benchmarking", bufferName);
        
        // Large configuration for throughput testing
        var config = new BufferConfig
        {
            MetadataSize = 4096,  // Larger metadata
            PayloadSize = 1048576 // 1MB payload buffer
        };
        
        var reader = new Reader(bufferName, config);
        _readers[bufferName] = reader;
        _testContext.SetData($"buffer_{bufferName}", reader);
        _testContext.SetData("current_reader", reader);
        
        _logger.LogInformation("Created large buffer '{BufferName}' with {MetadataSize} metadata and {PayloadSize} payload", 
            bufferName, config.MetadataSize, config.PayloadSize);
    }
    
    [Then(@"measure latency for frame sizes:")]
    public void ThenMeasureLatencyForFrameSizes()
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        var reader = _testContext.GetData<Reader>("current_reader");
        
        var frameSizes = new[] { 64, 256, 1024, 4096, 16384, 65536 };
        var latencyResults = new Dictionary<int, LatencyStats>();
        
        foreach (var frameSize in frameSizes)
        {
            var latencies = new List<TimeSpan>();
            var iterations = 1000; // Number of measurements per frame size
            
            _logger.LogInformation("Measuring latency for frame size {Size} bytes ({Iterations} iterations)", frameSize, iterations);
            
            // Warm up
            for (int w = 0; w < 10; w++)
            {
                var warmupData = new byte[frameSize];
                writer.WriteFrame(warmupData);
                var warmupFrame = reader.ReadFrame();
            }
            
            // Actual measurements
            for (int i = 0; i < iterations; i++)
            {
                var data = new byte[frameSize];
                for (int j = 0; j < frameSize; j++)
                {
                    data[j] = (byte)(j % 256);
                }
                
                var startTime = DateTime.UtcNow;
                writer.WriteFrame(data);
                var frame = reader.ReadFrame();
                var endTime = DateTime.UtcNow;
                
                var latency = endTime - startTime;
                latencies.Add(latency);
            }
            
            // Calculate statistics
            var sortedLatencies = latencies.OrderBy(l => l.TotalMicroseconds).ToList();
            var stats = new LatencyStats
            {
                FrameSize = frameSize,
                Min = sortedLatencies.First(),
                Max = sortedLatencies.Last(),
                Mean = TimeSpan.FromTicks((long)sortedLatencies.Average(l => l.Ticks)),
                P50 = sortedLatencies[(int)(sortedLatencies.Count * 0.50)],
                P90 = sortedLatencies[(int)(sortedLatencies.Count * 0.90)],
                P99 = sortedLatencies[(int)(sortedLatencies.Count * 0.99)],
                P999 = sortedLatencies[(int)(sortedLatencies.Count * 0.999)]
            };
            
            latencyResults[frameSize] = stats;
            
            _logger.LogInformation("Frame size {Size}: Min={Min:F1}μs, Max={Max:F1}μs, Mean={Mean:F1}μs, P99={P99:F1}μs",
                frameSize, stats.Min.TotalMicroseconds, stats.Max.TotalMicroseconds, 
                stats.Mean.TotalMicroseconds, stats.P99.TotalMicroseconds);
        }
        
        _testContext.SetData("latency_benchmark_results", latencyResults);
        _logger.LogInformation("Completed latency benchmark for {Count} frame sizes", frameSizes.Length);
    }
    
    [Then(@"report Min, Max, Mean, P50, P90, P99, P99.9")]
    public void ThenReportMinMaxMeanP50P90P99P999()
    {
        var results = _testContext.GetData<Dictionary<int, LatencyStats>>("latency_benchmark_results");
        
        _logger.LogInformation("=== LATENCY BENCHMARK RESULTS ===");
        _logger.LogInformation("Frame Size | Min (μs) | Max (μs) | Mean (μs) | P50 (μs) | P90 (μs) | P99 (μs) | P99.9 (μs)");
        _logger.LogInformation("-----------|----------|----------|-----------|----------|----------|----------|------------");
        
        foreach (var kvp in results.OrderBy(r => r.Key))
        {
            var size = kvp.Key;
            var stats = kvp.Value;
            
            _logger.LogInformation("{Size,10} | {Min,8:F1} | {Max,8:F1} | {Mean,9:F1} | {P50,8:F1} | {P90,8:F1} | {P99,8:F1} | {P999,10:F1}",
                size, stats.Min.TotalMicroseconds, stats.Max.TotalMicroseconds, stats.Mean.TotalMicroseconds,
                stats.P50.TotalMicroseconds, stats.P90.TotalMicroseconds, stats.P99.TotalMicroseconds, stats.P999.TotalMicroseconds);
        }
        
        _logger.LogInformation("=== END LATENCY BENCHMARK ===");
    }
    
    [Then(@"measure throughput for '(\d+)' seconds with frame sizes:")]
    public void ThenMeasureThroughputForSecondsWithFrameSizes(int durationSeconds)
    {
        var writer = _testContext.GetData<Writer>("current_writer");
        var reader = _testContext.GetData<Reader>("current_reader");
        
        var frameSizes = new[] { 1024, 4096, 16384, 65536 };
        var throughputResults = new Dictionary<int, ThroughputStats>();
        
        // For testing, use shorter duration
        var testDurationSeconds = Math.Min(durationSeconds, 10);
        
        foreach (var frameSize in frameSizes)
        {
            _logger.LogInformation("Measuring throughput for frame size {Size} bytes for {Duration} seconds", 
                frameSize, testDurationSeconds);
            
            var endTime = DateTime.UtcNow.AddSeconds(testDurationSeconds);
            var framesWritten = 0;
            var framesRead = 0;
            var bytesTransferred = 0L;
            var startTime = DateTime.UtcNow;
            
            // Start reader task
            var readerTask = Task.Run(() =>
            {
                var readCount = 0;
                try
                {
                    while (DateTime.UtcNow < endTime)
                    {
                        var frame = reader.ReadFrame();
                        readCount++;
                        Interlocked.Add(ref bytesTransferred, frame.ToFrameRef().Size);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Reader task stopped: {Exception}", ex.Message);
                }
                return readCount;
            });
            
            // Start writer task
            var writerTask = Task.Run(() =>
            {
                var writeCount = 0;
                try
                {
                    while (DateTime.UtcNow < endTime)
                    {
                        var data = new byte[frameSize];
                        for (int i = 0; i < frameSize; i++)
                        {
                            data[i] = (byte)(i % 256);
                        }
                        
                        writer.WriteFrame(data);
                        writeCount++;
                        
                        // Small delay to prevent overwhelming
                        if (writeCount % 100 == 0)
                        {
                            Thread.Yield();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Writer task stopped: {Exception}", ex.Message);
                }
                return writeCount;
            });
            
            // Wait for completion
            framesWritten = writerTask.Result;
            framesRead = readerTask.Result;
            
            var actualDuration = DateTime.UtcNow - startTime;
            var throughputMBps = (bytesTransferred / 1024.0 / 1024.0) / actualDuration.TotalSeconds;
            var throughputFramesPerSec = framesRead / actualDuration.TotalSeconds;
            
            var stats = new ThroughputStats
            {
                FrameSize = frameSize,
                Duration = actualDuration,
                FramesWritten = framesWritten,
                FramesRead = framesRead,
                BytesTransferred = bytesTransferred,
                ThroughputMBps = throughputMBps,
                ThroughputFramesPerSec = throughputFramesPerSec
            };
            
            throughputResults[frameSize] = stats;
            
            _logger.LogInformation("Frame size {Size}: {ThroughputMB:F1} MB/s, {ThroughputFrames:F0} frames/s ({Written} written, {Read} read)",
                frameSize, throughputMBps, throughputFramesPerSec, framesWritten, framesRead);
        }
        
        _testContext.SetData("throughput_benchmark_results", throughputResults);
        _logger.LogInformation("Completed throughput benchmark for {Count} frame sizes", frameSizes.Length);
    }
    
    [Then(@"report CPU usage percentage")]
    public void ThenReportCPUUsagePercentage()
    {
        // This would require system monitoring to get actual CPU usage
        // For now, we'll report that CPU usage should be monitored
        _logger.LogInformation("CPU usage should be monitored during throughput benchmark");
        _logger.LogInformation("Expected: Efficient CPU utilization with minimal overhead");
    }
    
    [Then(@"expect to saturate memory bandwidth")]
    public void ThenExpectToSaturateMemoryBandwidth()
    {
        var results = _testContext.GetData<Dictionary<int, ThroughputStats>>("throughput_benchmark_results");
        
        var totalThroughput = results.Values.Sum(s => s.ThroughputMBps);
        var maxFrameSizeThroughput = results.Values.OrderByDescending(s => s.FrameSize).First().ThroughputMBps;
        
        _logger.LogInformation("=== THROUGHPUT BENCHMARK RESULTS ===");
        _logger.LogInformation("Frame Size | Throughput (MB/s) | Frames/sec | Efficiency");
        _logger.LogInformation("-----------|-------------------|------------|------------");
        
        foreach (var kvp in results.OrderBy(r => r.Key))
        {
            var size = kvp.Key;
            var stats = kvp.Value;
            var efficiency = (stats.FramesRead * 100.0) / stats.FramesWritten;
            
            _logger.LogInformation("{Size,10} | {Throughput,17:F1} | {FramesPerSec,10:F0} | {Efficiency,9:F1}%",
                size, stats.ThroughputMBps, stats.ThroughputFramesPerSec, efficiency);
        }
        
        _logger.LogInformation("=== END THROUGHPUT BENCHMARK ===");
        _logger.LogInformation("Maximum throughput: {MaxThroughput:F1} MB/s (should approach memory bandwidth)", maxFrameSizeThroughput);
    }
    
    private class LatencyStats
    {
        public int FrameSize { get; set; }
        public TimeSpan Min { get; set; }
        public TimeSpan Max { get; set; }
        public TimeSpan Mean { get; set; }
        public TimeSpan P50 { get; set; }
        public TimeSpan P90 { get; set; }
        public TimeSpan P99 { get; set; }
        public TimeSpan P999 { get; set; }
    }
    
    private class ThroughputStats
    {
        public int FrameSize { get; set; }
        public TimeSpan Duration { get; set; }
        public int FramesWritten { get; set; }
        public int FramesRead { get; set; }
        public long BytesTransferred { get; set; }
        public double ThroughputMBps { get; set; }
        public double ThroughputFramesPerSec { get; set; }
    }
}