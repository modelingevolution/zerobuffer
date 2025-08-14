using System;

namespace ZeroBuffer.DuplexChannel
{
    /// <summary>
    /// Client implementation for duplex communication
    /// </summary>
    internal class DuplexClient : IDuplexClient
    {
        private readonly string _channelName;
        private readonly Writer _requestWriter;
        private readonly Reader _responseReader;
        private readonly BufferConfig _config;
        private bool _disposed;
        
        public DuplexClient(string channelName)
        {
            _channelName = channelName;
            _config = new BufferConfig(4096, 256 * 1024 * 1024); // 256MB buffer
            
            // Channel naming convention:
            // Request: {channelName}_request (client writes, server reads)
            // Response: {channelName}_response (server writes, client reads)
            var requestBufferName = $"{channelName}_request";
            var responseBufferName = $"{channelName}_response";
            
            try
            {
                // Connect to request buffer as writer (server creates this)
                _requestWriter = new Writer(requestBufferName);
                
                // Create response buffer as reader (we own this)
                _responseReader = new Reader(responseBufferName, _config);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        
        public bool IsServerConnected => _requestWriter?.IsReaderConnected() ?? false;
        
        public ulong SendRequest(byte[] data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplexClient));
                
            if (data == null)
                throw new ArgumentNullException(nameof(data));
                
            // Use zero-copy write to get the sequence number
            var buffer = _requestWriter.GetFrameBuffer(data.Length, out ulong sequenceNumber);
            data.AsSpan().CopyTo(buffer);
            _requestWriter.CommitFrame();
            
            return sequenceNumber;
        }
        
        public ulong AcquireRequestBuffer(int size, out Span<byte> buffer)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplexClient));
                
            // Get frame buffer and sequence number
            buffer = _requestWriter.GetFrameBuffer(size, out ulong sequenceNumber);
            
            return sequenceNumber;
        }
        
        public void CommitRequest()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplexClient));
                
            _requestWriter.CommitFrame();
        }
        
        public Frame ReceiveResponse(TimeSpan timeout)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DuplexClient));
                
            // Read a response frame directly (v1.0.0 - no sequence prefix)
            return _responseReader.ReadFrame(timeout);
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            
            _requestWriter?.Dispose();
            _responseReader?.Dispose();
        }
    }
}