using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Server implementation that processes immutable requests and returns new response data
    /// </summary>
    internal class ImmutableDuplexServer : IImmutableDuplexServer
    {
        private readonly string _channelName;
        private readonly BufferConfig _config;
        private readonly ILogger<ImmutableDuplexServer> _logger;
        private Reader _requestReader;
        private Writer _responseWriter;
        private Thread _processingThread;
        private CancellationTokenSource _cancellationTokenSource;
        private volatile bool _isRunning;
        private bool _disposed;
        
        public ImmutableDuplexServer(string channelName, BufferConfig config, ILogger<ImmutableDuplexServer>? logger = null)
        {
            _channelName = channelName;
            _config = config;
            _logger = logger ?? NullLogger<ImmutableDuplexServer>.Instance;
        }
        
        public bool IsRunning => _isRunning;
        
        public void Start(RequestHandler handler, ProcessingMode mode = ProcessingMode.SingleThread)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ImmutableDuplexServer));
                
            if (_isRunning)
                throw new InvalidOperationException("Server is already running");
                
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
                
            // Channel naming convention:
            var requestBufferName = $"{_channelName}_request";
            var responseBufferName = $"{_channelName}_response";
            
            try
            {
                // Create request buffer as reader (we own this)
                _requestReader = new Reader(requestBufferName, _config);
                
                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;
                
                // Start processing based on mode
                if (mode == ProcessingMode.SingleThread)
                {
                    // Start processing thread - it will connect to response buffer when available
                    _processingThread = new Thread(() => ProcessRequests(handler, responseBufferName, _cancellationTokenSource.Token))
                    {
                        Name = $"DuplexServer_{_channelName}",
                        IsBackground = true
                    };
                    _processingThread.Start();
                }
                else
                {
                    // ThreadPool mode not yet implemented
                    throw new NotSupportedException($"Processing mode {mode} is not yet implemented");
                }
            }
            catch
            {
                Stop();
                throw;
            }
        }
        
        public void Stop()
        {
            if (!_isRunning)
                return;
                
            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            
            // Wait for processing thread to complete
            _processingThread?.Join(TimeSpan.FromSeconds(5));
            
            // Clean up resources
            _cancellationTokenSource?.Dispose();
            _responseWriter?.Dispose();
            _requestReader?.Dispose();
            
            _cancellationTokenSource = null;
            _processingThread = null;
            _responseWriter = null;
            _requestReader = null;
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            Stop();
        }
        
        private Writer ConnectToResponseBuffer(string bufferName)
        {
            const int maxRetries = 50; // 5 second timeout
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return new Writer(bufferName);
                }
                catch (BufferNotFoundException)
                {
                    Thread.Sleep(100);
                }
            }
            
            throw new TimeoutException($"Timeout waiting for response buffer {bufferName}");
        }
        
        private void ProcessRequests(RequestHandler handler, string responseBufferName, CancellationToken cancellationToken)
        {
            // Connect to response buffer when it becomes available
            if (_responseWriter == null)
            {
                try
                {
                    _responseWriter = ConnectToResponseBuffer(responseBufferName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to response buffer {BufferName}", responseBufferName);
                    return;
                }
            }
            
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Read request with timeout - using ensures Dispose is called
                    using var request = _requestReader.ReadFrame(TimeSpan.FromSeconds(1));
                    if (!request.IsValid)
                        continue;
                    
                    // Process request and get response data
                    ReadOnlySpan<byte> responseData;
                    try
                    {
                        responseData = handler(request);
                    }
                    catch (Exception ex)
                    {
                        // Log error and send empty response
                        _logger.LogError(ex, "Error in request handler for channel {ChannelName}", _channelName);
                        responseData = ReadOnlySpan<byte>.Empty;
                    }
                    
                    // Write response directly without sequence prefix (v1.0.0 protocol)
                    // The sequence is managed internally by Reader/Writer
                    _responseWriter.WriteFrame(responseData);
                }
                catch (ReaderDeadException)
                {
                    // Client disconnected
                    break;
                }
                catch (WriterDeadException)
                {
                    // Client's response reader died
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // Log unexpected errors
                    _logger.LogError(ex, "Server processing error on channel {ChannelName}", _channelName);
                }
            }
        }
    }
}