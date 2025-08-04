using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using CommandLine;
using Microsoft.Extensions.Logging;
using ZeroBuffer;

namespace ZeroBuffer.CrossPlatform
{
    public class TestReader
    {
        private readonly ILogger<TestReader> _logger;

        public TestReader(ILogger<TestReader> logger)
        {
            _logger = logger;
        }

        public int Run(ReaderOptions options)
        {
            var result = new TestResult
            {
                Operation = "read",
                BufferName = options.BufferName,
                FramesRead = 0,
                FrameSize = 0,
                MetadataSize = 0,
                DurationSeconds = 0.0,
                ThroughputMbps = 0.0,
                VerificationErrors = 0,
                Checksums = new List<ChecksumEntry>(),
                Errors = new List<string>()
            };

            try
            {
                if (options.Verbose && !options.JsonOutput)
                {
                    Console.WriteLine($"[READER] Connecting to buffer: {options.BufferName}");
                }

                // Create buffer with fixed config matching C++ implementation
                var config = new BufferConfig(4096, 256 * 1024 * 1024);
                
                // Create logger for Reader
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder
                        .AddConsole()
                        .SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
                });
                var readerLogger = loggerFactory.CreateLogger<Reader>();
                
                using (var reader = new Reader(options.BufferName, config, readerLogger))
                {
                    if (options.Verbose && !options.JsonOutput)
                    {
                        Console.WriteLine($"[READER] Successfully created buffer");
                    }

                    // Read metadata if available
                    var metadata = reader.GetMetadata();
                    if (metadata.Length > 0)
                    {
                        result.MetadataSize = metadata.Length;
                        if (options.Verbose && !options.JsonOutput)
                        {
                            Console.WriteLine($"[READER] Read metadata: {metadata.Length} bytes");
                        }
                    }

                    // Read frames
                    var stopwatch = Stopwatch.StartNew();
                    int framesToRead = options.Frames > 0 ? options.Frames : int.MaxValue;
                    int frameIndex = 0;

                    if (options.Verbose && !options.JsonOutput)
                    {
                        Console.WriteLine($"[READER] Starting to read frames...");
                    }

                    while (frameIndex < framesToRead)
                    {
                        try
                        {
                            // Read frame with timeout
                            var frame = reader.ReadFrame(TimeSpan.FromMilliseconds(options.TimeoutMs));

                            if (!frame.IsValid)
                            {
                                // Timeout or no more frames
                                if (options.Verbose && !options.JsonOutput)
                                {
                                    Console.WriteLine($"[READER] No more frames after {frameIndex} frames");
                                }
                                break;
                            }

                            // Track frame size from first frame
                            if (result.FrameSize == 0)
                            {
                                result.FrameSize = frame.Size;
                            }

                            // Verify frame size
                            if (frame.Size != result.FrameSize)
                            {
                                result.Errors.Add(
                                    $"Frame {frameIndex}: Expected size {result.FrameSize}, got {frame.Size}"
                                );
                            }

                            // Verify data pattern if requested
                            if (options.Verify != "none")
                            {
                                var frameData = frame.ToArray();
                                if (!VerifyFrameData(frameData, frameIndex, options.Verify))
                                {
                                    result.VerificationErrors++;
                                    if (options.Verbose && !options.JsonOutput)
                                    {
                                        Console.WriteLine($"[READER] Frame {frameIndex}: Verification failed");
                                    }
                                }
                            }

                            // Calculate checksum if requested
                            if (options.Checksum)
                            {
                                var checksum = CalculateChecksum(frame.ToArray());
                                if (result.Checksums.Count < 100) // Limit stored checksums
                                {
                                    result.Checksums.Add(new ChecksumEntry
                                    {
                                        Frame = frameIndex,
                                        Checksum = checksum
                                    });
                                }
                            }

                            frameIndex++;
                            result.FramesRead = frameIndex;

                            if (options.Verbose && !options.JsonOutput)
                            {
                                if (frameIndex % 10 == 0 || frameIndex == 1 || frameIndex == framesToRead)
                                {
                                    Console.WriteLine($"[READER] Read frame {frameIndex}");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            result.Errors.Add($"Frame {frameIndex}: {e.Message}");
                            break;
                        }
                    }

                    stopwatch.Stop();
                    result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;

                    // Calculate throughput
                    double totalMb = (result.FramesRead * result.FrameSize) / (1024.0 * 1024.0);
                    result.ThroughputMbps = result.DurationSeconds > 0 ? totalMb / result.DurationSeconds : 0;

                    if (!options.JsonOutput)
                    {
                        Console.WriteLine($"[READER] Completed: read {result.FramesRead} frames in {result.DurationSeconds:F2} seconds");
                        Console.WriteLine($"[READER] Throughput: {result.ThroughputMbps:F2} MB/s");
                        if (options.Verify != "none")
                        {
                            Console.WriteLine($"[READER] Verification errors: {result.VerificationErrors}");
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
                    Console.Error.WriteLine($"[READER] Error: {e.Message}");
                }

                return 2;
            }

            if (options.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }

            return (result.VerificationErrors == 0 && !result.Errors.Any()) ? 0 : 1;
        }

        private bool VerifyFrameData(byte[] data, int frameIndex, string pattern)
        {
            switch (pattern)
            {
                case "sequential":
                    for (int i = 0; i < data.Length; i++)
                    {
                        byte expected = (byte)((frameIndex + i) % 256);
                        if (data[i] != expected)
                        {
                            return false;
                        }
                    }
                    return true;

                case "random":
                    var random = new Random(frameIndex);
                    for (int i = 0; i < data.Length; i++)
                    {
                        byte expected = (byte)random.Next(256);
                        if (data[i] != expected)
                        {
                            return false;
                        }
                    }
                    return true;

                case "zero":
                    return data.All(b => b == 0);

                case "ones":
                    return data.All(b => b == 0xFF);

                case "none":
                    return true;

                default:
                    throw new ArgumentException($"Unknown pattern: {pattern}");
            }
        }

        private string CalculateChecksum(byte[] data)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private class TestResult
        {
            public string Operation { get; set; }
            public string BufferName { get; set; }
            public int FramesRead { get; set; }
            public int FrameSize { get; set; }
            public int MetadataSize { get; set; }
            public double DurationSeconds { get; set; }
            public double ThroughputMbps { get; set; }
            public int VerificationErrors { get; set; }
            public List<ChecksumEntry> Checksums { get; set; }
            public List<string> Errors { get; set; }
        }

        private class ChecksumEntry
        {
            public int Frame { get; set; }
            public string Checksum { get; set; }
        }
    }

    // Update ReaderOptions in Program.cs to match
    public partial class ReaderOptions : BaseOptions
    {
        [Option('s', "size", Default = 1024, HelpText = "Expected size of each frame in bytes")]
        public int Size { get; set; }

        [Option("batch-size", Default = 1, HelpText = "Read frames in batches")]
        public int BatchSize { get; set; }

        [Option("verify", Default = "none", HelpText = "Verify data pattern: none|sequential|random|zero|ones")]
        public string Verify { get; set; } = "none";

        [Option("checksum", Default = false, HelpText = "Calculate checksums for each frame")]
        public bool Checksum { get; set; }
    }
}