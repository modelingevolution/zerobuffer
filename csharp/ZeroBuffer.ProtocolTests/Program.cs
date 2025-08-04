using System.Diagnostics;
using CommandLine;
using StreamJsonRpc;
using ZeroBuffer.ProtocolTests.JsonRpc;

namespace ZeroBuffer.ProtocolTests
{
    class Program
    {
        [Verb("run", HelpText = "Run a test")]
        public class RunOptions
        {
            [Option('m', "mode", Required = true, HelpText = "Execution mode: same-process, separate-process, or cross-platform")]
            public string Mode { get; set; } = "";
            
            [Option('t', "test", Required = true, HelpText = "Test ID (e.g., 101 for test 1.1)")]
            public int TestId { get; set; }
            
            [Option('r', "role", Required = true, HelpText = "Role: writer, reader, or both (both only for same-process)")]
            public string Role { get; set; } = "";
            
            [Option('b', "buffer", Required = false, Default = null, HelpText = "Buffer name (auto-generated if not specified)")]
            public string? BufferName { get; set; }
        }
        
        [Verb("list", HelpText = "List all available tests")]
        public class ListOptions
        {
        }
        
        [Verb("serve", HelpText = "Start JSON-RPC server on stdin/stdout")]
        public class ServeOptions
        {
        }
        
        [Verb("test-jsonrpc", HelpText = "Test JSON-RPC infrastructure")]
        public class TestJsonRpcOptions
        {
        }
        
        static async Task<int> Main(string[] args)
        {
            // Initialize test registry
            TestRegistry.Initialize();
            
            return await Parser.Default.ParseArguments<RunOptions, ListOptions, ServeOptions, TestJsonRpcOptions>(args)
                .MapResult(
                    (RunOptions opts) => RunTestAsync(opts),
                    (ListOptions opts) => ListTestsAsync(opts),
                    (ServeOptions opts) => ServeJsonRpcAsync(opts),
                    (TestJsonRpcOptions opts) => TestJsonRpcAsync(opts),
                    errs => HandleParseError(errs));
        }
        
        static Task<int> ListTestsAsync(ListOptions options)
        {
            ListAllTests();
            return Task.FromResult(0);
        }
        
        static async Task<int> RunTestAsync(RunOptions options)
        {
            
            // Validate options
            if (!IsValidMode(options.Mode))
            {
                Console.Error.WriteLine($"Invalid mode: {options.Mode}. Valid modes: same-process, separate-process, cross-platform");
                return 1;
            }
            
            if (!IsValidRole(options.Role, options.Mode))
            {
                Console.Error.WriteLine($"Invalid role: {options.Role} for mode {options.Mode}");
                return 1;
            }
            
            // Get test
            var test = TestRegistry.GetTest(options.TestId);
            if (test == null)
            {
                Console.Error.WriteLine($"Test {options.TestId} not found");
                ListAllTests();
                return 1;
            }
            
            // Generate buffer name if not provided
            var bufferName = options.BufferName ?? $"test-{options.TestId}-{Guid.NewGuid():N}";
            
            Console.WriteLine($"Running test {test.TestId}: {test.Description}");
            Console.WriteLine($"Mode: {options.Mode}, Role: {options.Role}, Buffer: {bufferName}");
            Console.WriteLine();
            
            try
            {
                return options.Mode switch
                {
                    "same-process" => await RunSameProcessAsync(test, bufferName),
                    "separate-process" => await RunSeparateProcessAsync(test, options.Role, bufferName),
                    "cross-platform" => await RunCrossPlatformAsync(test, options.Role, bufferName),
                    _ => 1
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Test failed with exception: {ex}");
                return 1;
            }
        }
        
        static async Task<int> RunSameProcessAsync(IProtocolTest test, string bufferName)
        {
            var result = await test.RunBothAsync(bufferName);
            Console.WriteLine($"\nTest {test.TestId} {(result == 0 ? "PASSED" : "FAILED")}");
            return result;
        }
        
        static async Task<int> RunSeparateProcessAsync(IProtocolTest test, string role, string bufferName)
        {
            if (role == "both")
            {
                // Spawn both reader and writer processes
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "ZeroBuffer.ProtocolTests";
                
                var readerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--mode separate-process --test {test.TestId} --role reader --buffer {bufferName}",
                    UseShellExecute = false
                });
                
                var writerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--mode separate-process --test {test.TestId} --role writer --buffer {bufferName}",
                    UseShellExecute = false
                });
                
                if (readerProcess == null || writerProcess == null)
                {
                    Console.Error.WriteLine("Failed to start child processes");
                    return 1;
                }
                
                await Task.WhenAll(
                    readerProcess.WaitForExitAsync(),
                    writerProcess.WaitForExitAsync()
                );
                
                var success = readerProcess.ExitCode == 0 && writerProcess.ExitCode == 0;
                Console.WriteLine($"\nTest {test.TestId} {(success ? "PASSED" : "FAILED")}");
                Console.WriteLine($"Reader exit code: {readerProcess.ExitCode}");
                Console.WriteLine($"Writer exit code: {writerProcess.ExitCode}");
                
                return success ? 0 : 1;
            }
            else
            {
                // Run specific role
                var result = role switch
                {
                    "reader" => await test.RunReaderAsync(bufferName),
                    "writer" => await test.RunWriterAsync(bufferName),
                    _ => 1
                };
                
                Console.WriteLine($"\n{role} completed with result: {result}");
                return result;
            }
        }
        
        static async Task<int> RunCrossPlatformAsync(IProtocolTest test, string role, string bufferName)
        {
            // For cross-platform, we just run our role
            // The other side should be started by the test harness with appropriate language
            var result = role switch
            {
                "reader" => await test.RunReaderAsync(bufferName),
                "writer" => await test.RunWriterAsync(bufferName),
                _ => 1
            };
            
            Console.WriteLine($"\n{role} completed with result: {result}");
            return result;
        }
        
        static bool IsValidMode(string mode)
        {
            return mode is "same-process" or "separate-process" or "cross-platform";
        }
        
        static bool IsValidRole(string role, string mode)
        {
            return role switch
            {
                "both" => mode == "same-process" || mode == "separate-process",
                "reader" or "writer" => true,
                _ => false
            };
        }
        
        static void ListAllTests()
        {
            Console.WriteLine("\nAvailable tests:");
            foreach (var test in TestRegistry.GetAllTests())
            {
                Console.WriteLine($"  {test.TestId}: {test.Description}");
            }
        }
        
        static Task<int> HandleParseError(IEnumerable<Error> errors)
        {
            return Task.FromResult(1);
        }
        
        static async Task<int> ServeJsonRpcAsync(ServeOptions options)
        {
            try
            {
                Console.Error.WriteLine("Starting JSON-RPC server on stdin/stdout...");
                
                using var stdin = Console.OpenStandardInput();
                using var stdout = Console.OpenStandardOutput();
                
                var service = new TestService();
                using var rpc = new StreamJsonRpc.JsonRpc(stdout, stdin);
                
                // Register service methods
                rpc.AddLocalRpcTarget(service, new JsonRpcTargetOptions 
                { 
                    MethodNameTransform = CommonMethodNameTransforms.CamelCase 
                });
                
                // Start listening
                rpc.StartListening();
                Console.Error.WriteLine("JSON-RPC server started. Waiting for commands...");
                
                // Wait until the connection is closed
                await rpc.Completion;
                
                Console.Error.WriteLine("JSON-RPC server stopped.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"JSON-RPC server error: {ex}");
                return 1;
            }
        }
        
        static Task<int> TestJsonRpcAsync(TestJsonRpcOptions options)
        {
            try
            {
                JsonRpc.SimpleTest.RunInProcessTest();
                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Test failed: {ex}");
                return Task.FromResult(1);
            }
        }
    }
}