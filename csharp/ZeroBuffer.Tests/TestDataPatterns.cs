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
        /// Generate simple test data for a frame based only on size
        /// Used when sequence number is not known at write time
        /// </summary>
        public static byte[] GenerateSimpleFrameData(int size)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)(i % 256);
            }
            return data;
        }

        /// <summary>
        /// Verify that frame data matches the simple pattern
        /// </summary>
        public static bool VerifySimpleFrameData(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != (byte)(i % 256))
                    return false;
            }
            return true;
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