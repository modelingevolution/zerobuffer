using System;

namespace ZeroBuffer
{
    /// <summary>
    /// Base exception for ZeroBuffer operations
    /// </summary>
    public class ZeroBufferException : Exception
    {
        public ZeroBufferException(string message) : base(message) { }
        public ZeroBufferException(string message, Exception innerException) 
            : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when the buffer is full and cannot accept more frames
    /// </summary>
    public class BufferFullException : ZeroBufferException
    {
        public BufferFullException() 
            : base("Buffer is full") { }
    }

    /// <summary>
    /// Thrown when a frame is too large for the buffer
    /// </summary>
    public class FrameTooLargeException : ZeroBufferException
    {
        public FrameTooLargeException() 
            : base("Frame size exceeds buffer capacity") { }
    }

    /// <summary>
    /// Thrown when the writer process has died
    /// </summary>
    public class WriterDeadException : ZeroBufferException
    {
        public WriterDeadException() 
            : base("Writer process is dead") { }
    }

    /// <summary>
    /// Thrown when the reader process has died
    /// </summary>
    public class ReaderDeadException : ZeroBufferException
    {
        public ReaderDeadException() 
            : base("Reader process is dead") { }
    }

    /// <summary>
    /// Thrown when trying to connect to a non-existent buffer
    /// </summary>
    public class BufferNotFoundException : ZeroBufferException
    {
        public BufferNotFoundException(string message) 
            : base(message) { }
            
        public BufferNotFoundException(string bufferName, bool isName) 
            : base($"Buffer '{bufferName}' not found") { }
    }

    /// <summary>
    /// Thrown when another writer is already connected
    /// </summary>
    public class WriterAlreadyConnectedException : ZeroBufferException
    {
        public WriterAlreadyConnectedException() 
            : base("Another writer is already connected") { }
    }

    /// <summary>
    /// Thrown when another reader is already connected
    /// </summary>
    public class ReaderAlreadyConnectedException : ZeroBufferException
    {
        public ReaderAlreadyConnectedException() 
            : base("Another reader is already connected") { }
    }
}