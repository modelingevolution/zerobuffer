using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Shared;
using Xunit;

namespace ZeroBuffer.Cpp.Integration.Tests;

public class InitializationContextTests : IDisposable
{
    private Process? _serverProcess;
    private JsonRpc? _jsonRpc;
    private Stream? _stream;

    [Fact]
    public async Task Initialize_ShouldStoreInitializationContext()
    {
        // Arrange
        await StartServerAsync();
        var initRequest = new InitializeRequest(
            Role: "writer",
            Platform: "cpp",
            Scenario: "TestScenario123",
            HostPid: 54321,
            FeatureId: 42
        );

        // Act
        var result = await _jsonRpc!.InvokeAsync<bool>("initialize", initRequest);

        // Assert
        result.Should().BeTrue();
        
        // Verify context is stored by executing a step that uses it
        var stepRequest = new StepRequest(
            Process: "writer",
            StepType: StepType.Given,
            Step: "the test environment is initialized",
            Parameters: ImmutableDictionary<string, string>.Empty
                .Add("testParam1", "value1")
                .Add("testParam2", "123"),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        var stepResponse = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", stepRequest);
        stepResponse.Should().NotBeNull();
        stepResponse.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteStep_ShouldReceiveAndStoreParameters()
    {
        // Arrange
        await StartServerAsync();
        await _jsonRpc!.InvokeAsync<bool>("initialize", 
            new InitializeRequest("reader", "cpp", "ParamTest", 11111, 22));

        var stepRequest = new StepRequest(
            Process: "reader",
            StepType: StepType.When,
            Step: "the reader process creates buffer test-buffer with default configuration",
            Parameters: ImmutableDictionary<string, string>.Empty
                .Add("bufferSize", "1024")
                .Add("timeout", "5000")
                .Add("retryCount", "3")
                .Add("enableLogging", "true"),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        // Act
        var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", stepRequest);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Error.Should().BeNullOrEmpty();
        
        // Execute another step to verify parameters were stored
        var verifyStep = new StepRequest(
            Process: "reader",
            StepType: StepType.Then,
            Step: "the reader should have a valid buffer",
            Parameters: ImmutableDictionary<string, string>.Empty,
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        var verifyResponse = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", verifyStep);
        verifyResponse.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_MultipleCalls_ShouldResetContext()
    {
        // Arrange
        await StartServerAsync();
        
        // First initialization
        var firstInit = new InitializeRequest("writer", "cpp", "FirstTest", 111, 1);
        await _jsonRpc!.InvokeAsync<bool>("initialize", firstInit);
        
        // Second initialization should reset context
        var secondInit = new InitializeRequest("reader", "python", "SecondTest", 222, 2);
        
        // Act
        var result = await _jsonRpc!.InvokeAsync<bool>("initialize", secondInit);
        
        // Assert
        result.Should().BeTrue();
        
        // Verify new context is active by executing a step
        var stepRequest = new StepRequest(
            Process: "reader",
            StepType: StepType.Given,
            Step: "the test environment is initialized",
            Parameters: ImmutableDictionary<string, string>.Empty.Add("param", "test"),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        var stepResponse = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", stepRequest);
        stepResponse.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteStep_WithComplexParameters_ShouldHandleCorrectly()
    {
        // Arrange
        await StartServerAsync();
        await _jsonRpc!.InvokeAsync<bool>("initialize", 
            new InitializeRequest("writer", "cpp", "ComplexParamTest", 33333, 44));

        var stepRequest = new StepRequest(
            Process: "writer",
            StepType: StepType.When,
            Step: "the writer process writes hello, world! to the buffer",
            Parameters: ImmutableDictionary<string, string>.Empty
                .Add("text", "hello, world!")
                .Add("nestedObject", "{\"prop1\":\"value1\",\"prop2\":42}")
                .Add("arrayParam", "[1,2,3,4,5]")
                .Add("boolParam", "true")
                .Add("nullParam", ""),
            Context: ImmutableDictionary<string, string>.Empty,
            IsBroadcast: false
        );

        // Act
        var response = await _jsonRpc!.InvokeAsync<StepResponse>("executeStep", stepRequest);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Error.Should().BeNullOrEmpty();
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