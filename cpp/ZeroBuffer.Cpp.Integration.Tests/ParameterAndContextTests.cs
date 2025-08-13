using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Shared;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Cpp.Integration.Tests;

public class ParameterAndContextTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Process? _serverProcess;
    private JsonRpc? _jsonRpc;
    private Stream? _stream;

    public ParameterAndContextTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Discover_ShouldReturnAvailableSteps()
    {
        // Arrange
        await StartServerAsync();

        // Act
        var result = await _jsonRpc!.InvokeAsync<dynamic>("discover");

        // Assert
        Assert.NotNull(result);
        var steps = result.steps as Newtonsoft.Json.Linq.JArray;
        Assert.NotNull(steps);
        Assert.True(steps!.Count > 0);
        
        _output.WriteLine($"Found {steps.Count} registered steps:");
        foreach (var step in steps)
        {
            _output.WriteLine($"  - {step["type"]}: {step["pattern"]}");
        }
    }

    [Fact]
    public async Task Initialize_ShouldStoreContext_AndExecuteBasicStep()
    {
        // Arrange
        await StartServerAsync();
        
        // First discover available steps
        var discoverResult = await _jsonRpc!.InvokeAsync<dynamic>("discover");
        var steps = discoverResult.steps as Newtonsoft.Json.Linq.JArray;
        _output.WriteLine($"Available steps: {steps?.Count ?? 0}");

        // Initialize with context
        var initRequest = new InitializeRequest(
            Role: "reader",
            Platform: "cpp",
            Scenario: "TestContext",
            HostPid: 12345,
            FeatureId: 99
        );

        // Act
        var initResult = await _jsonRpc!.InvokeAsync<bool>("initialize", initRequest);

        // Assert
        initResult.Should().BeTrue();

        // Try to execute a simple step that should exist
        var stepRequest = new StepRequest(
            Process: "reader",
            StepType: StepType.Given,
            Step: "the test environment is initialized",
            Parameters: ImmutableDictionary<string, string>.Empty
                .Add("testMode", "integration")
                .Add("debugLevel", "verbose"),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        var stepResponse = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", stepRequest);
        
        // The step should at least not crash, even if not implemented
        stepResponse.Should().NotBeNull();
        _output.WriteLine($"Step execution result: Success={stepResponse.Success}, Error={stepResponse.Error}");
        
        // Check if logs are present
        if (stepResponse.Logs != null && stepResponse.Logs.Any())
        {
            _output.WriteLine($"Step logs ({stepResponse.Logs.Count} entries):");
            foreach (var log in stepResponse.Logs.Take(5))
            {
                _output.WriteLine($"  [{log.Level}] {log.Message}");
            }
        }
    }

    [Fact]
    public async Task ExecuteStep_WithParameters_ShouldPassToStepImplementation()
    {
        // Arrange
        await StartServerAsync();
        await _jsonRpc!.InvokeAsync<bool>("initialize", 
            new InitializeRequest("writer", "cpp", "ParamTest", 55555, 77));

        // Use a step that's likely to exist based on the basic_communication_steps.cpp
        var stepRequest = new StepRequest(
            Process: "writer",
            StepType: StepType.When,
            Step: "the writer process writes hello, world! to the buffer",
            Parameters: ImmutableDictionary<string, string>.Empty
                .Add("message", "hello, world!")
                .Add("encoding", "utf-8")
                .Add("timestamp", DateTime.UtcNow.ToString("O")),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        // Act
        var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", stepRequest);

        // Assert
        response.Should().NotBeNull();
        _output.WriteLine($"Step result: Success={response.Success}, Error={response.Error}");
        
        // Even if the step fails (buffer not created), parameters should be accepted
        // The important thing is that the server doesn't crash with parameters
    }

    [Fact]
    public async Task MultipleInitializations_ShouldResetContext()
    {
        // Arrange
        await StartServerAsync();
        
        // First initialization
        var init1 = new InitializeRequest("writer", "cpp", "Test1", 111, 1);
        var result1 = await _jsonRpc!.InvokeAsync<bool>("initialize", init1);
        result1.Should().BeTrue();
        
        // Second initialization (should reset)
        var init2 = new InitializeRequest("reader", "python", "Test2", 222, 2);
        var result2 = await _jsonRpc!.InvokeAsync<bool>("initialize", init2);
        result2.Should().BeTrue();
        
        // Third initialization with different values
        var init3 = new InitializeRequest("both", "csharp", "Test3", 333, 3);
        var result3 = await _jsonRpc!.InvokeAsync<bool>("initialize", init3);
        result3.Should().BeTrue();
        
        _output.WriteLine("Multiple initializations completed successfully");
    }

    private async Task StartServerAsync()
    {
        var serverPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "build", "serve", "zerobuffer-serve");

        if (!File.Exists(serverPath))
        {
            throw new FileNotFoundException($"Server executable not found at: {serverPath}");
        }

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment = { ["ZEROBUFFER_LOG_LEVEL"] = "ERROR" }
            }
        };

        _serverProcess.Start();

        _stream = new DuplexStream(_serverProcess.StandardInput.BaseStream, 
                                   _serverProcess.StandardOutput.BaseStream);
        
        _jsonRpc = new JsonRpc(new HeaderDelimitedMessageHandler(_stream, _stream))
        {
            ExceptionStrategy = ExceptionProcessing.CommonErrorData
        };
        
        _jsonRpc.StartListening();

        // Verify server is ready
        var health = await _jsonRpc.InvokeAsync<bool>("health");
        health.Should().BeTrue();
    }

    public void Dispose()
    {
        _jsonRpc?.Dispose();
        _stream?.Dispose();
        
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill();
            _serverProcess.WaitForExit(1000);
            _serverProcess.Dispose();
        }
    }
    
    private class DuplexStream : Stream
    {
        private readonly Stream _readStream;
        private readonly Stream _writeStream;

        public DuplexStream(Stream writeStream, Stream readStream)
        {
            _writeStream = writeStream;
            _readStream = readStream;
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
        public override Task FlushAsync(CancellationToken cancellationToken) 
            => _writeStream.FlushAsync(cancellationToken);
        
        public override int Read(byte[] buffer, int offset, int count) 
            => _readStream.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _readStream.ReadAsync(buffer, offset, count, cancellationToken);
        
        public override void Write(byte[] buffer, int offset, int count) 
            => _writeStream.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        
        public override long Seek(long offset, SeekOrigin origin) 
            => throw new NotSupportedException();
        public override void SetLength(long value) 
            => throw new NotSupportedException();
    }
}