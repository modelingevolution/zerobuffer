using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroBuffer.Benchmarks
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TimestampedFrame
    {
        public long Timestamp;  // 8 bytes - Stopwatch ticks
        public int FrameId;     // 4 bytes
        public int Padding;     // 4 bytes - align to 16 bytes
        // Rest is frame data
    }

    public static class CrossProcessPerformanceTests
    {
        private const int HEADER_SIZE = 16; // sizeof(TimestampedFrame)
        private const int FULLHD_1080P_SIZE = 1920 * 1080 * 3 / 2; // 3,110,400 bytes
        private const int TOTAL_FRAME_SIZE = HEADER_SIZE + FULLHD_1080P_SIZE;
        private const int WARMUP_FRAMES = 100;
        private const int TEST_FRAMES_PER_FPS = 1000; // Measure 1000 frames at each FPS

        public static async Task RunAllTests()
        {
            Console.WriteLine($"Frame size: YUV420 Full HD + timestamp header ({TOTAL_FRAME_SIZE:N0} bytes)");
            Console.WriteLine($"Warmup frames: {WARMUP_FRAMES}");
            Console.WriteLine($"Test frames per FPS: {TEST_FRAMES_PER_FPS}");
            Console.WriteLine();

            // Test at various FPS levels
            var fpsTargets = new[] { 30, 60, 120, 240, 500, 1000 };

            foreach (var fps in fpsTargets)
            {
                Console.WriteLine($"--- Testing at {fps} FPS ---");
                await MeasureLatency(fps);
                Console.WriteLine();
                
                // Small delay between tests
                await Task.Delay(1000);
            }
        }

        private static async Task MeasureLatency(int targetFps)
        {
            var testId = Guid.NewGuid().ToString("N");
            var bufferToRelay = $"bench-to-relay-{testId}";
            var bufferFromRelay = $"bench-from-relay-{testId}";
            
            // Start relay process
            var relayProcess = Process.Start(new ProcessStartInfo
            {
                FileName = GetTestHelperPath(),
                Arguments = $"relay {bufferToRelay} {bufferFromRelay}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,  // Don't capture output for performance
                RedirectStandardError = true
            });

            if (relayProcess == null)
            {
                throw new InvalidOperationException("Failed to start relay process");
            }

            await Task.Delay(1000); // Let relay initialize and create its buffer

            try
            {
                await RunBenchmark(bufferToRelay, bufferFromRelay, targetFps);
            }
            finally
            {
                // Stop relay process
                relayProcess.Kill();
                await relayProcess.WaitForExitAsync();
            }
        }

        private static async Task RunBenchmark(string bufferToRelay, string bufferFromRelay, int targetFps)
        {
            var config = new BufferConfig(4096, 256 * 1024 * 1024); // 256MB buffer
            var latencies = new List<double>();
            
            // Prepare frame data
            var frameData = new byte[TOTAL_FRAME_SIZE];
            Random.Shared.NextBytes(frameData.AsSpan(HEADER_SIZE)); // Random video data after header
            
            // Step 3: Create our output buffer as reader (relay will connect as writer)
            using var readerFromRelay = new Reader(bufferFromRelay, config);
            
            // Give relay time to connect to our output buffer
            await Task.Delay(500);
            
            // Step 4: Connect to relay's input buffer as writer (relay creates this as reader)
            Writer writerToRelay = null;
            for (int retry = 0; retry < 20; retry++) // 2 second timeout
            {
                try
                {
                    writerToRelay = new Writer(bufferToRelay);
                    break;
                }
                catch (BufferNotFoundException)
                {
                    await Task.Delay(100);
                }
            }
            
            if (writerToRelay == null)
            {
                throw new InvalidOperationException($"Failed to connect to relay input buffer: {bufferToRelay}");
            }
            
            using (writerToRelay)
            {
                
                var frameInterval = TimeSpan.FromSeconds(1.0 / targetFps);
                
                // Use PeriodicTimer for precise timing (minimum 1ms)
                var timerInterval = frameInterval < TimeSpan.FromMilliseconds(1) 
                    ? TimeSpan.FromMilliseconds(1) 
                    : frameInterval;
                using var timer = new PeriodicTimer(timerInterval);
                
                // Warmup
                Console.Write("  Warming up...");
                for (int i = 0; i < WARMUP_FRAMES; i++)
                {
                    WriteTimestampedFrame(writerToRelay, frameData, i);
                    await timer.WaitForNextTickAsync();
                    
                    // Try to read response
                    using var response = readerFromRelay.ReadFrame(TimeSpan.FromMilliseconds(10));
                    if (!response.IsValid)
                    {
                        // During warmup, relay might not be ready yet
                        continue;
                    }
                }
                Console.WriteLine(" done");
                
                // Clear any pending frames
                while (readerFromRelay.ReadFrame(TimeSpan.Zero).IsValid) { }
                
                // Main test
                Console.Write($"  Measuring {TEST_FRAMES_PER_FPS} frames...");
                var framesSent = 0;
                var framesReceived = 0;
                
                for (int i = 0; i < TEST_FRAMES_PER_FPS; i++)
                {
                    // Send frame with timestamp
                    var sendTimestamp = Stopwatch.GetTimestamp();
                    WriteTimestampedFrame(writerToRelay, frameData, i, sendTimestamp);
                    framesSent++;

                    // Try to read response immediately
                    using (var response = readerFromRelay.ReadFrame(TimeSpan.FromMilliseconds(50)))
                    {
                        if (response.IsValid)
                        {
                            var receiveTimestamp = Stopwatch.GetTimestamp();

                            // Extract timestamp from response
                            if (response.Size >= HEADER_SIZE)
                            {
                                var responseData = response.ToArray();
                                var originalTimestamp = BitConverter.ToInt64(responseData, 0);

                                // Calculate round-trip latency
                                var latencyTicks = receiveTimestamp - originalTimestamp;
                                var latencyUs = (latencyTicks * 1_000_000.0) / Stopwatch.Frequency;
                                latencies.Add(latencyUs);
                                framesReceived++;
                            }
                        }
                    }

                    await timer.WaitForNextTickAsync();
                }
                
                // Read any remaining responses
                await Task.Delay(100);
                while (true)
                {
                    using var response = readerFromRelay.ReadFrame(TimeSpan.Zero);
                    if (!response.IsValid)
                        break;
                        
                    var receiveTimestamp = Stopwatch.GetTimestamp();
                    if (response.Size >= HEADER_SIZE)
                    {
                        var responseData = response.ToArray();
                        var originalTimestamp = BitConverter.ToInt64(responseData, 0);
                        var latencyTicks = receiveTimestamp - originalTimestamp;
                        var latencyUs = (latencyTicks * 1_000_000.0) / Stopwatch.Frequency;
                        latencies.Add(latencyUs);
                        framesReceived++;
                    }
                }
                
                Console.WriteLine(" done");
                
                // Print results
                Console.WriteLine($"  Frames sent: {framesSent}, received: {framesReceived}");
                
                if (latencies.Count > 0)
                {
                    latencies.Sort();
                    var min = latencies.Min();
                    var max = latencies.Max();
                    var avg = latencies.Average();
                    var p50 = GetPercentile(latencies, 0.50);
                    var p90 = GetPercentile(latencies, 0.90);
                    var p99 = GetPercentile(latencies, 0.99);
                    
                    Console.WriteLine($"  Round-trip latency (microseconds):");
                    Console.WriteLine($"    Min:  {min,8:F0} μs");
                    Console.WriteLine($"    Avg:  {avg,8:F0} μs");
                    Console.WriteLine($"    P50:  {p50,8:F0} μs");
                    Console.WriteLine($"    P90:  {p90,8:F0} μs");  
                    Console.WriteLine($"    P99:  {p99,8:F0} μs");
                    Console.WriteLine($"    Max:  {max,8:F0} μs");
                }
                else
                {
                    Console.WriteLine("  ERROR: No latency measurements collected!");
                }
            }
        }

        private static void WriteTimestampedFrame(Writer writer, byte[] frameData, int frameId, long timestamp = 0)
        {
            if (timestamp == 0)
                timestamp = Stopwatch.GetTimestamp();
                
            // Write timestamp and frame ID to the beginning of the frame
            BitConverter.TryWriteBytes(frameData.AsSpan(0, 8), timestamp);
            BitConverter.TryWriteBytes(frameData.AsSpan(8, 4), frameId);
            
            writer.WriteFrame(frameData);
        }

        private static double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
                return 0;
                
            var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
            if (index < 0) index = 0;
            if (index >= sortedValues.Count) index = sortedValues.Count - 1;
            
            return sortedValues[index];
        }

        private static string GetTestHelperPath()
        {
            var currentDir = Directory.GetCurrentDirectory();
            var helperExe = Path.Combine(currentDir, "ZeroBuffer.TestHelper");
            
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                helperExe += ".exe";
                
            if (!File.Exists(helperExe))
            {
                throw new FileNotFoundException($"TestHelper not found at: {helperExe}. Please build ZeroBuffer.TestHelper project first.");
            }
                
            return helperExe;
        }
    }
}