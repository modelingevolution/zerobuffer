using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using ZeroBuffer;

namespace ZeroBuffer.TestHelper
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: TestHelper <mode> <buffer-name> [args...]");
                Console.WriteLine("Modes: reader, reader-with-validation, writer, benchmark-*");
                return;
            }

            var mode = args[0];
            var bufferName = args[1];

            try
            {
                switch (mode)
                {
                    case "reader":
                        RunReader(bufferName, args);
                        break;
                    case "reader-with-validation":
                        RunReaderWithValidation(bufferName, args);
                        break;
                    case "writer":
                        RunWriter(bufferName, args);
                        break;
                    case "benchmark-latency-reader":
                        RunLatencyBenchmarkReader(bufferName, args);
                        break;
                    case "benchmark-latency-writer":
                        RunLatencyBenchmarkWriter(bufferName, args);
                        break;
                    case "benchmark-throughput-reader":
                        RunThroughputBenchmarkReader(bufferName, args);
                        break;
                    case "benchmark-throughput-writer":
                        RunThroughputBenchmarkWriter(bufferName, args);
                        break;
                    case "relay":
                        RunRelay(bufferName, args);
                        break;
                    default:
                        Console.WriteLine($"Unknown mode: {mode}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void RunReader(string bufferName, string[] args)
        {
            // Parse config from args if provided
            var metadataSize = args.Length > 2 ? int.Parse(args[2]) : 1024;
            var payloadSize = args.Length > 3 ? int.Parse(args[3]) : 64 * 1024;
            
            var config = new BufferConfig(metadataSize, payloadSize);
            
            using var reader = new Reader(bufferName, config);
            
            // Read frames continuously
            while (true)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                if (!frame.IsValid)
                    break;
                    
                Console.WriteLine($"Read frame {frame.Sequence}, size: {frame.Size}");
                
                // Check if this is an EXIT command
                var data = frame.ToArray();
                var text = System.Text.Encoding.UTF8.GetString(data);
                if (text == "EXIT")
                {
                    Console.WriteLine("Received EXIT command");
                    break;
                }
                
                Thread.Sleep(100); // Simulate processing
            }
        }

        static void RunReaderWithValidation(string bufferName, string[] args)
        {
            // Parse config from args if provided
            var metadataSize = args.Length > 2 ? int.Parse(args[2]) : 1024;
            var payloadSize = args.Length > 3 ? int.Parse(args[3]) : 64 * 1024;
            
            var config = new BufferConfig(metadataSize, payloadSize);
            
            using var reader = new Reader(bufferName, config);
            
            // Read frames continuously with hash validation
            while (true)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                if (!frame.IsValid)
                    break;
                    
                // Calculate hash of frame data
                var hash = Convert.ToBase64String(SHA256.HashData(frame.Span));
                Console.WriteLine($"Read frame {frame.Sequence}, size: {frame.Size}");
                Console.WriteLine($"Hash: {hash}");
                
                // Check if this is an EXIT command
                if (frame.Size < 10) // EXIT is short
                {
                    var data = frame.ToArray();
                    var text = System.Text.Encoding.UTF8.GetString(data);
                    if (text == "EXIT")
                    {
                        Console.WriteLine("Received EXIT command");
                        break;
                    }
                }
                
                Thread.Sleep(10); // Simulate processing
            }
        }

        static void RunWriter(string bufferName, string[] args)
        {
            using var writer = new Writer(bufferName);
            
            // Write frames continuously
            var frameData = new byte[1024];
            var sequence = 0;
            
            while (sequence < 10)
            {
                frameData[0] = (byte)sequence;
                writer.WriteFrame(frameData);
                Console.WriteLine($"Wrote frame {sequence}");
                sequence++;
                Thread.Sleep(100);
            }
        }

        static void RunLatencyBenchmarkReader(string bufferName, string[] args)
        {
            var frameSize = int.Parse(args[2]);
            var frameCount = args.Length > 3 ? int.Parse(args[3]) : 100;
            
            var config = new BufferConfig(4096, 100 * 1024 * 1024); // 100MB buffer
            using var reader = new Reader(bufferName, config);
            
            // Warmup
            for (int i = 0; i < 10; i++)
            {
                var warmupFrame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                if (!warmupFrame.IsValid) break;
            }
            
            // Measure latency for each frame
            var totalLatencyMicros = 0.0;
            var measurements = 0;
            
            for (int i = 0; i < frameCount; i++)
            {
                var sw = Stopwatch.StartNew();
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                sw.Stop();
                
                if (!frame.IsValid) break;
                
                var latencyMicros = sw.Elapsed.TotalMicroseconds;
                totalLatencyMicros += latencyMicros;
                measurements++;
            }
            
            var avgLatency = totalLatencyMicros / measurements;
            Console.WriteLine($"LATENCY:{avgLatency:F2}");
            Console.WriteLine($"Frames read: {measurements}");
        }

        static void RunLatencyBenchmarkWriter(string bufferName, string[] args)
        {
            var frameSize = int.Parse(args[2]);
            var frameCount = args.Length > 3 ? int.Parse(args[3]) : 100;
            
            Thread.Sleep(100); // Let reader initialize
            
            using var writer = new Writer(bufferName);
            var frameData = new byte[frameSize];
            Random.Shared.NextBytes(frameData);
            
            // Warmup
            for (int i = 0; i < 10; i++)
            {
                writer.WriteFrame(frameData);
            }
            
            // Write frames for benchmark
            for (int i = 0; i < frameCount; i++)
            {
                writer.WriteFrame(frameData);
                Thread.Sleep(1); // Small delay to prevent overwhelming
            }
        }

        static void RunThroughputBenchmarkReader(string bufferName, string[] args)
        {
            var frameSize = int.Parse(args[2]);
            var totalFrames = int.Parse(args[3]);
            
            var config = new BufferConfig(4096, 256 * 1024 * 1024); // 256MB buffer
            using var reader = new Reader(bufferName, config);
            
            var sw = Stopwatch.StartNew();
            var framesRead = 0;
            
            while (framesRead < totalFrames)
            {
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(10));
                if (!frame.IsValid) break;
                framesRead++;
            }
            
            sw.Stop();
            
            var fps = framesRead / sw.Elapsed.TotalSeconds;
            var mbps = (framesRead * frameSize) / (sw.Elapsed.TotalSeconds * 1024 * 1024);
            
            // Calculate YUV420 Full HD equivalents
            const int YUV420_FULLHD_SIZE = 1920 * 1080 * 3 / 2; // 3,110,400 bytes
            var yuv420FullHdEquivalentFps = fps * ((double)frameSize / YUV420_FULLHD_SIZE);
            
            Console.WriteLine($"THROUGHPUT_FPS:{fps:F2}");
            Console.WriteLine($"THROUGHPUT_MBPS:{mbps:F2}");
            Console.WriteLine($"THROUGHPUT_YUV420_FULLHD_FPS:{yuv420FullHdEquivalentFps:F2}");
            Console.WriteLine($"Total frames: {framesRead}");
            Console.WriteLine($"Frame size: {frameSize} bytes");
            Console.WriteLine($"YUV420 FullHD frame size: {YUV420_FULLHD_SIZE} bytes");
            Console.WriteLine($"Total time: {sw.Elapsed.TotalSeconds:F2}s");
        }

        static void RunThroughputBenchmarkWriter(string bufferName, string[] args)
        {
            var frameSize = int.Parse(args[2]);
            var totalFrames = int.Parse(args[3]);
            var targetFps = args.Length > 4 ? int.Parse(args[4]) : 30;
            
            Thread.Sleep(200); // Let reader initialize
            
            using var writer = new Writer(bufferName);
            var frameData = new byte[frameSize];
            Random.Shared.NextBytes(frameData);
            
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);
            var nextFrame = DateTime.UtcNow;
            
            for (int i = 0; i < totalFrames; i++)
            {
                writer.WriteFrame(frameData);
                
                nextFrame += frameInterval;
                var delay = nextFrame - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    Thread.Sleep(delay);
                }
            }
            
            Console.WriteLine($"Wrote {totalFrames} frames at target {targetFps} FPS");
        }

        static void RunRelay(string inputBufferName, string[] args)
        {
            // Args: relay <input-buffer> <output-buffer>
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: TestHelper relay <input-buffer> <output-buffer>");
                Environment.Exit(1);
            }

            var outputBufferName = args[2];
            
            Console.WriteLine($"Starting relay: {inputBufferName} -> {outputBufferName}");
            
            // Step 1: Create input buffer (we own this)
            var config = new BufferConfig(4096, 256 * 1024 * 1024); // 256MB buffer
            using var reader = new Reader(inputBufferName, config);
            Console.WriteLine($"Created input buffer: {inputBufferName}");
            
            // Step 2: Wait for output buffer to be created by benchmark process
            Writer writer = null;
            const int maxRetries = 50; // 5 second timeout
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    writer = new Writer(outputBufferName);
                    Console.WriteLine($"Connected to output buffer: {outputBufferName}");
                    break;
                }
                catch (BufferNotFoundException)
                {
                    if (i == 0)
                        Console.WriteLine($"Waiting for output buffer {outputBufferName} to be created...");
                    Thread.Sleep(100);
                }
            }
            
            if (writer == null)
            {
                Console.WriteLine($"ERROR: Timeout waiting for output buffer {outputBufferName}");
                Environment.Exit(1);
            }
            
            using (writer)
            {
                Console.WriteLine("Relay ready, starting echo loop");
                
                // Simple relay loop - copy frames as fast as possible
                var framesRelayed = 0;
                var lastStatusTime = DateTime.UtcNow;
                
                while (true)
                {
                    try
                    {
                        var frame = reader.ReadFrame(TimeSpan.FromSeconds(1));
                        if (!frame.IsValid)
                            continue;
                        
                        // Write the frame to output buffer (including timestamp)
                        writer.WriteFrame(frame.Span);
                        framesRelayed++;
                        
                        // Print status every second
                        var now = DateTime.UtcNow;
                        if ((now - lastStatusTime).TotalSeconds >= 1.0)
                        {
                            Console.WriteLine($"Relayed {framesRelayed} frames");
                            lastStatusTime = now;
                        }
                    }
                    catch (WriterDeadException)
                    {
                        Console.WriteLine("Benchmark process terminated");
                        break;
                    }
                    catch (ReaderDeadException)
                    {
                        Console.WriteLine("Writer terminated unexpectedly");
                        break;
                    }
                }
                
                Console.WriteLine($"Relay completed. Total frames relayed: {framesRelayed}");
            }
        }
    }
}