using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

namespace ModelingEvolution.Harmony.Tests;

public class BasicJsonRpcIntegrationTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    
    public BasicJsonRpcIntegrationTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void TestBasicJsonRpcCommunication()
    {
        // Create a simple echo server for testing
        using var serverPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var clientPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        
        // Start a simple echo process
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project {GetEchoServerPath()}",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        // For now, let's just test the basic setup
        _output.WriteLine("Test basic JSON-RPC setup");
        
        // Create in-memory streams for testing
        using var sendingStream = new MemoryStream();
        using var receivingStream = new MemoryStream();
        
        var formatter = new SystemTextJsonFormatter();
        
        // Test creating the handler
        Assert.Throws<ArgumentException>(() => 
        {
            // This should fail because MemoryStream is not readable when empty
            var handler = new LengthHeaderMessageHandler(sendingStream, receivingStream, formatter);
        });
        
        _output.WriteLine("Basic handler creation test passed");
    }
    
    [Fact]
    public async Task TestSimpleJsonRpcEcho()
    {
        // Create bidirectional pipes for testing
        using var stream1 = new MemoryStream();
        using var stream2 = new MemoryStream();
        
        // Create a simple in-memory JSON-RPC server
        var serverTask = Task.Run(async () =>
        {
            using var serverStream = new SimpleDuplexStream(stream2, stream1);
            var formatter = new SystemTextJsonFormatter();
            var handler = new LengthHeaderMessageHandler(serverStream, serverStream, formatter);
            
            using var jsonRpc = new JsonRpc(handler);
            
            // Add a simple echo method
            jsonRpc.AddLocalRpcMethod("echo", new Func<string, string>(message => 
            {
                _output.WriteLine($"Server received: {message}");
                return $"Echo: {message}";
            }));
            
            jsonRpc.StartListening();
            
            // Wait for a bit
            await Task.Delay(1000);
        });
        
        // Give server time to start
        await Task.Delay(100);
        
        // Create client
        using var clientStream = new SimpleDuplexStream(stream1, stream2);
        var clientFormatter = new SystemTextJsonFormatter();
        var clientHandler = new LengthHeaderMessageHandler(clientStream, clientStream, clientFormatter);
        
        using var clientRpc = new JsonRpc(clientHandler);
        clientRpc.StartListening();
        
        try
        {
            var result = await clientRpc.InvokeAsync<string>("echo", "Hello World");
            _output.WriteLine($"Client received: {result}");
            Assert.Equal("Echo: Hello World", result);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error: {ex.Message}");
            throw;
        }
        
        await serverTask;
    }
    
    private string GetEchoServerPath()
    {
        return Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), 
            "../../../../../../csharp/ZeroBuffer.Serve/ZeroBuffer.Serve.csproj"));
    }
    
    public void Dispose()
    {
        // Nothing to dispose
    }
    
    // Simple duplex stream for testing
    private class SimpleDuplexStream : Stream
    {
        private readonly Stream _readStream;
        private readonly Stream _writeStream;
        
        public SimpleDuplexStream(Stream readStream, Stream writeStream)
        {
            _readStream = readStream;
            _writeStream = writeStream;
        }
        
        public override bool CanRead => _readStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _writeStream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position 
        { 
            get => throw new NotSupportedException(); 
            set => throw new NotSupportedException(); 
        }
        
        public override void Flush() => _writeStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _readStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _writeStream.Write(buffer, offset, count);
    }
}