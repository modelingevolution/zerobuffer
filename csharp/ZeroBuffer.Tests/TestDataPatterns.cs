using System;

namespace ZeroBuffer.Tests
{
    /// <summary>
    /// Shared test data patterns for consistent data generation across processes
    /// </summary>
    public static class TestDataPatterns
    {
        /// <summary>
        /// Generate test data for a frame based on size and sequence number
        /// </summary>
        public static byte[] GenerateFrameData(int size, ulong sequence)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)(((ulong)i + sequence) % 256);
            }
            return data;
        }

        /// <summary>
        /// Generate test metadata based on size
        /// </summary>
        public static byte[] GenerateMetadata(int size)
        {
            var metadata = new byte[size];
            for (int i = 0; i < size; i++)
            {
                metadata[i] = (byte)(i % 256);
            }
            return metadata;
        }
    }
}