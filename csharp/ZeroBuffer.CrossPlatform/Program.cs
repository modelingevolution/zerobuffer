using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using CommandLine;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZeroBuffer;

namespace ZeroBuffer.CrossPlatform
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ZeroBuffer.CrossPlatform <command> [options]");
                Console.WriteLine("Commands: writer, reader, relay");
                return 3;
            }

            var command = args[0];
            var commandArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();

            return command switch
            {
                "writer" => RunWriter(commandArgs),
                "reader" => RunReader(commandArgs),
                "relay" => RunRelay(commandArgs),
                _ => HandleUnknownCommand(command)
            };
        }

        static int HandleUnknownCommand(string command)
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            Console.WriteLine("Valid commands: writer, reader, relay");
            return 3;
        }

        static int RunWriter(string[] args)
        {
            return Parser.Default.ParseArguments<WriterOptions>(args)
                .MapResult(
                    options => new TestWriter().Run(options),
                    errors => 3);
        }

        static int RunReader(string[] args)
        {
            return Parser.Default.ParseArguments<ReaderOptions>(args)
                .MapResult(
                    options =>
                    {
                        using var loggerFactory = LoggerFactory.Create(builder =>
                        {
                            builder.AddConsole();
                        });
                        var logger = loggerFactory.CreateLogger<TestReader>();
                        return new TestReader(logger).Run(options);
                    },
                    errors => 3);
        }

        static int RunRelay(string[] args)
        {
            return Parser.Default.ParseArguments<RelayOptions>(args)
                .MapResult(
                    options => new TestRelay().Run(options),
                    errors => 3);
        }
    }

    // Base options
    public abstract class BaseOptions
    {
        [Option("json-output", Required = false, HelpText = "Output results in JSON format")]
        public bool JsonOutput { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Verbose output")]
        public bool Verbose { get; set; }
    }

    // Writer options
    [Verb("writer", HelpText = "Write frames to a buffer")]
    public class WriterOptions : BaseOptions
    {
        [Value(0, Required = true, HelpText = "Name of the buffer to write to")]
        public string BufferName { get; set; } = "";

        [Option('n', "frames", Default = 1000, HelpText = "Number of frames to write")]
        public int Frames { get; set; }

        [Option('s', "size", Default = 1024, HelpText = "Size of each frame in bytes")]
        public int FrameSize { get; set; }

        [Option('m', "metadata", HelpText = "Metadata to write")]
        public string? Metadata { get; set; }

        [Option("metadata-file", HelpText = "Read metadata from file")]
        public string? MetadataFile { get; set; }

        [Option("pattern", Default = "sequential", HelpText = "Data pattern: sequential|random|zero|ones")]
        public string Pattern { get; set; } = "sequential";

        [Option("delay-ms", Default = 0, HelpText = "Delay between frames in milliseconds")]
        public int DelayMs { get; set; }

        [Option("batch-size", Default = 1, HelpText = "Write frames in batches")]
        public int BatchSize { get; set; }
    }

    // Reader options
    [Verb("reader", HelpText = "Read frames from a buffer")]
    public partial class ReaderOptions : BaseOptions
    {
        [Value(0, Required = true, HelpText = "Name of the buffer to read from")]
        public string BufferName { get; set; } = "";

        [Option('n', "frames", Default = 0, HelpText = "Number of frames to read (0 for unlimited)")]
        public int Frames { get; set; }

        [Option("timeout-ms", Default = 5000, HelpText = "Timeout per frame in milliseconds")]
        public int TimeoutMs { get; set; }
    }

    // Relay options
    [Verb("relay", HelpText = "Relay frames between buffers")]
    public partial class RelayOptions : BaseOptions
    {
        [Value(0, Required = true, HelpText = "Name of the input buffer")]
        public string InputBuffer { get; set; } = "";

        [Value(1, Required = true, HelpText = "Name of the output buffer")]
        public string OutputBuffer { get; set; } = "";

        [Option('n', "frames", Default = 0, HelpText = "Number of frames to relay (0 for unlimited)")]
        public int Frames { get; set; }

        [Option("create-output", HelpText = "Create output buffer if it doesn't exist")]
        public bool CreateOutput { get; set; }

        [Option("buffer-size", Default = 256 * 1024 * 1024, HelpText = "Output buffer size when creating (default: 256MB)")]
        public int BufferSize { get; set; } = 256 * 1024 * 1024;

        [Option("timeout-ms", Default = 5000, HelpText = "Timeout per frame in milliseconds")]
        public int TimeoutMs { get; set; }

        [Option("transform", Default = "none", HelpText = "Apply transformation: none|reverse|xor")]
        public string Transform { get; set; } = "none";

        [Option("xor-key", Default = (byte)0xFF, HelpText = "XOR key for transform")]
        public byte XorKey { get; set; } = 0xFF;
    }
}