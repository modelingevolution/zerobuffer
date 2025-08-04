namespace ZeroBuffer.ProtocolTests
{
    /// <summary>
    /// Interface for protocol test scenarios
    /// </summary>
    public interface IProtocolTest
    {
        /// <summary>
        /// Test identifier (e.g., 101 for test 1.1)
        /// </summary>
        int TestId { get; }
        
        /// <summary>
        /// Test description
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Run the writer side of the test
        /// </summary>
        /// <param name="bufferName">Buffer name to use</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>0 for success, non-zero for failure</returns>
        Task<int> RunWriterAsync(string bufferName, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Run the reader side of the test
        /// </summary>
        /// <param name="bufferName">Buffer name to use</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>0 for success, non-zero for failure</returns>
        Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Run both sides in the same process
        /// </summary>
        /// <param name="bufferName">Buffer name to use</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>0 for success, non-zero for failure</returns>
        Task<int> RunBothAsync(string bufferName, CancellationToken cancellationToken = default);
    }
}