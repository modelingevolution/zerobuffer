using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using ZeroBuffer;

namespace ZeroBuffer.CrossPlatform
{
    public class TestWriter
    {
        public int Run(WriterOptions options)
        {
            var result = new
            {
                operation = "write",
                buffer_name = options.BufferName,
                frames_written = 0,
                frame_size = options.FrameSize,
                metadata_size = 0,
                duration_seconds = 0.0,
                throughput_mbps = 0.0,
                errors = new List<string>()
            };

            try
            {
                if (options.Verbose && !options.JsonOutput)
                {
                    Console.WriteLine($"Connecting to buffer: {options.BufferName}");
                }

                using var writer = new Writer(options.BufferName);

                // Write metadata if provided
                byte[]? metadata = null;
                if (!string.IsNullOrEmpty(options.Metadata))
                {
                    metadata = Encoding.UTF8.GetBytes(options.Metadata);
                }
                else if (!string.IsNullOrEmpty(options.MetadataFile))
                {
                    metadata = File.ReadAllBytes(options.MetadataFile);
                }

                if (metadata != null)
                {
                    writer.SetMetadata(metadata);
                    result = result with { metadata_size = metadata.Length };
                    
                    if (options.Verbose && !options.JsonOutput)
                    {
                        Console.WriteLine($"Wrote metadata: {metadata.Length} bytes");
                    }
                }

                // Prepare frame data
                var frameData = new byte[options.FrameSize];
                var stopwatch = Stopwatch.StartNew();

                // Write frames
                for (int i = 0; i < options.Frames; i++)
                {
                    FillFrameData(frameData, i, options.Pattern);
                    writer.WriteFrame(frameData);
                    
                    result = result with { frames_written = i + 1 };

                    if (options.Verbose && !options.JsonOutput && (i + 1) % 100 == 0)
                    {
                        Console.WriteLine($"Wrote {i + 1} frames...");
                    }

                    if (options.DelayMs > 0)
                    {
                        Thread.Sleep(options.DelayMs);
                    }
                }

                stopwatch.Stop();
                var duration = stopwatch.Elapsed.TotalSeconds;
                var totalMB = (options.Frames * options.FrameSize) / (1024.0 * 1024.0);
                var throughput = totalMB / duration;

                result = result with
                {
                    duration_seconds = duration,
                    throughput_mbps = throughput
                };

                if (!options.JsonOutput)
                {
                    Console.WriteLine($"Wrote {options.Frames} frames in {duration:F2} seconds");
                    Console.WriteLine($"Throughput: {throughput:F2} MB/s");
                }
            }
            catch (Exception ex)
            {
                var errors = result.errors;
                errors.Add(ex.Message);
                result = result with { errors = errors };

                if (options.JsonOutput)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
                }
                else
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                }

                return 2;
            }

            if (options.JsonOutput)
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            }

            return 0;
        }

        private static void FillFrameData(byte[] data, int frameIndex, string pattern)
        {
            switch (pattern)
            {
                case "sequential":
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = (byte)((frameIndex + i) % 256);
                    }
                    break;

                case "random":
                    var rand = new Random(frameIndex);
                    rand.NextBytes(data);
                    break;

                case "zero":
                    Array.Fill(data, (byte)0);
                    break;

                case "ones":
                    Array.Fill(data, (byte)0xFF);
                    break;

                default:
                    throw new ArgumentException($"Unknown pattern: {pattern}");
            }
        }
    }
}