using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using ZeroBuffer;

namespace ZeroBuffer.CrossPlatform
{
    public class TestRelay
    {
        public int Run(RelayOptions options)
        {
            var result = new TestResult
            {
                Operation = "relay",
                InputBuffer = options.InputBuffer,
                OutputBuffer = options.OutputBuffer,
                FramesRelayed = 0,
                MetadataRelayed = false,
                DurationSeconds = 0.0,
                ThroughputMbps = 0.0,
                Errors = new List<string>()
            };

            try
            {
                if (options.Verbose && !options.JsonOutput)
                {
                    Console.WriteLine($"[RELAY] Starting relay from '{options.InputBuffer}' to '{options.OutputBuffer}'");
                    Console.WriteLine($"[RELAY] Frames to relay: {(options.Frames == 0 ? "unlimited" : options.Frames.ToString())}");
                    Console.WriteLine($"[RELAY] Timeout: {options.TimeoutMs}ms");
                    Console.WriteLine($"[RELAY] Output buffer size: {options.BufferSize / (1024 * 1024)}MB");
                }

                // Create input buffer as reader with standard config
                var config = new BufferConfig(4096, 256 * 1024 * 1024); // 256MB buffer
                using (var reader = new Reader(options.InputBuffer, config))
                {
                    if (options.Verbose && !options.JsonOutput)
                    {
                        Console.WriteLine($"[RELAY] Created input buffer: {options.InputBuffer}");
                    }

                    // Wait for output buffer to be created, then connect as writer
                    Writer writer = null;
                    const int maxRetries = 50; // 5 second timeout
                    
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            writer = new Writer(options.OutputBuffer);
                            if (options.Verbose && !options.JsonOutput)
                            {
                                Console.WriteLine($"[RELAY] Connected to output buffer: {options.OutputBuffer}");
                            }
                            break;
                        }
                        catch (BufferNotFoundException)
                        {
                            if (i == 0 && options.Verbose && !options.JsonOutput)
                            {
                                Console.WriteLine($"[RELAY] Waiting for output buffer {options.OutputBuffer} to be created...");
                            }
                            Thread.Sleep(100);
                        }
                    }
                    
                    if (writer == null)
                    {
                        result.Errors.Add($"Timeout waiting for output buffer {options.OutputBuffer}");
                        throw new InvalidOperationException($"Timeout waiting for output buffer {options.OutputBuffer}");
                    }

                    using (writer)
                    {
                        // Relay metadata if available
                        var metadata = reader.GetMetadata();
                        if (metadata.Length > 0)
                        {
                            writer.SetMetadata(metadata);
                            result.MetadataRelayed = true;
                            result.MetadataSize = metadata.Length;
                            
                            if (options.Verbose && !options.JsonOutput)
                            {
                                Console.WriteLine($"[RELAY] Relayed metadata: {metadata.Length} bytes");
                            }
                        }

                        // Relay frames
                        var stopwatch = Stopwatch.StartNew();
                        int framesToRelay = options.Frames > 0 ? options.Frames : int.MaxValue;
                        int frameIndex = 0;
                        long totalBytes = 0;

                        if (options.Verbose && !options.JsonOutput)
                        {
                            Console.WriteLine($"[RELAY] Starting frame relay...");
                        }

                        while (frameIndex < framesToRelay)
                        {
                            try
                            {
                                // Read frame from input
                                var frame = reader.ReadFrame(TimeSpan.FromMilliseconds(options.TimeoutMs));

                                if (!frame.IsValid)
                                {
                                    if (options.Verbose && !options.JsonOutput)
                                    {
                                        Console.WriteLine($"[RELAY] No more frames after {frameIndex} frames");
                                    }
                                    break;
                                }

                                // Write frame to output
                                writer.WriteFrame(frame.Span);
                                
                                frameIndex++;
                                totalBytes += frame.Size;
                                result.FramesRelayed = frameIndex;

                                if (options.Verbose && !options.JsonOutput && frameIndex % options.LogInterval == 0)
                                {
                                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                                    var throughput = (totalBytes / (1024.0 * 1024.0)) / elapsed;
                                    Console.WriteLine($"[RELAY] Progress: {frameIndex} frames, " +
                                                    $"{totalBytes / (1024.0 * 1024.0):F2}MB, " +
                                                    $"{throughput:F2}MB/s");
                                }
                            }
                            catch (TimeoutException)
                            {
                                if (options.Verbose && !options.JsonOutput)
                                {
                                    Console.WriteLine($"[RELAY] Read timeout after {frameIndex} frames");
                                }
                                break;
                            }
                            catch (Exception e)
                            {
                                result.Errors.Add($"Frame {frameIndex}: {e.Message}");
                                if (options.Verbose && !options.JsonOutput)
                                {
                                    Console.WriteLine($"[RELAY] Error at frame {frameIndex}: {e.Message}");
                                }
                                break;
                            }
                        }

                        stopwatch.Stop();
                        result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
                        result.TotalBytes = totalBytes;

                        // Calculate throughput
                        double totalMb = totalBytes / (1024.0 * 1024.0);
                        result.ThroughputMbps = result.DurationSeconds > 0 ? totalMb / result.DurationSeconds : 0;

                        if (options.Verbose && !options.JsonOutput)
                        {
                            Console.WriteLine($"[RELAY] Completed: {result.FramesRelayed} frames relayed");
                            Console.WriteLine($"[RELAY] Total data: {totalMb:F2}MB in {result.DurationSeconds:F2}s");
                            Console.WriteLine($"[RELAY] Throughput: {result.ThroughputMbps:F2}MB/s");
                        }

                        if (!options.JsonOutput)
                        {
                            Console.WriteLine($"Relayed {result.FramesRelayed} frames in {result.DurationSeconds:F2} seconds");
                            Console.WriteLine($"Throughput: {result.ThroughputMbps:F2} MB/s");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                result.Errors.Add(e.Message);

                if (options.JsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.Error.WriteLine($"[RELAY] Error: {e.Message}");
                }

                return 2;
            }

            if (options.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }

            return result.Errors.Any() ? 1 : 0;
        }

        private class TestResult
        {
            public string Operation { get; set; }
            public string InputBuffer { get; set; }
            public string OutputBuffer { get; set; }
            public int FramesRelayed { get; set; }
            public bool MetadataRelayed { get; set; }
            public int MetadataSize { get; set; }
            public long TotalBytes { get; set; }
            public double DurationSeconds { get; set; }
            public double ThroughputMbps { get; set; }
            public List<string> Errors { get; set; }
        }
    }

    // Update RelayOptions to add missing properties
    public partial class RelayOptions : BaseOptions
    {
        [Option("log-interval", Default = 100, HelpText = "Log progress every N frames")]
        public int LogInterval { get; set; } = 100;
    }
}