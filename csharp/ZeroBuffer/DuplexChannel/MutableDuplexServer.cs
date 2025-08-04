using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Server implementation that mutates request data in-place (zero-copy)
    /// </summary>
    internal class MutableDuplexServer : IMutableDuplexServer
    {
        private readonly string _channelName;
        private readonly BufferConfig _config;
        private readonly ILogger<MutableDuplexServer> _logger;
        private Reader _requestReader;
        private Writer _responseWriter;
        private Thread _processingThread;
        private CancellationTokenSource _cancellationTokenSource;
        private volatile bool _isRunning;
        private bool _disposed;
        
        public MutableDuplexServer(string channelName, BufferConfig config, ILogger<MutableDuplexServer>? logger = null)
        {
            _channelName = channelName;
            _config = config;
            _logger = logger ?? NullLogger<MutableDuplexServer>.Instance;
        }
        
        public bool IsRunning => _isRunning;
        
        public void Start(Action<Frame> handler, ProcessingMode mode = ProcessingMode.SingleThread)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MutableDuplexServer));
                
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
                        Name = $"MutableDuplexServer_{_channelName}",
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
        
        private void ProcessRequests(Action<Frame> handler, string responseBufferName, CancellationToken cancellationToken)
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
                    // Read request with timeout
                    var request = _requestReader.ReadFrame(TimeSpan.FromSeconds(1));
                    if (!request.IsValid)
                        continue;
                    
                    // For mutable processing, we need to make a copy because:
                    // 1. The request is in the request buffer (read-only for us)
                    // 2. We need to send the response to a different buffer
                    var responseData = request.ToArray();
                    
                    // Let handler modify the response data
                    try
                    {
                        // Create a frame wrapper around the response data
                        unsafe
                        {
                            fixed (byte* dataPtr = responseData)
                            {
                                var mutableFrame = new Frame(dataPtr, responseData.Length, request.Sequence);
                                handler(mutableFrame);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue processing
                        _logger.LogError(ex, "Error in mutable request handler for channel {ChannelName}", _channelName);
                    }
                    
                    // Send the modified data as response with sequence number prefix
                    var response = new byte[8 + responseData.Length];
                    BitConverter.TryWriteBytes(response.AsSpan(0, 8), (long)request.Sequence);
                    Buffer.BlockCopy(responseData, 0, response, 8, responseData.Length);
                    
                    _responseWriter.WriteFrame(response);
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