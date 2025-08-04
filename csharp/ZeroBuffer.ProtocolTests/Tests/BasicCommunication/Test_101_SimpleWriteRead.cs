namespace ZeroBuffer.ProtocolTests.Tests.BasicCommunication
{
    /// <summary>
    /// Test 1.1: Simple Write-Read Cycle
    /// </summary>
    public class Test_101_SimpleWriteRead : BaseProtocolTest
    {
        public override int TestId => 101;
        public override string Description => "Simple Write-Read Cycle";
        
        private const int MetadataSize = 1024;    // 1KB
        private const int PayloadSize = 10240;    // 10KB
        private const int MetadataTestSize = 100; // 100 bytes
        private const int FrameTestSize = 1024;   // 1KB
        
        public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
        {
            try
            {
                Log("Reader: Creating buffer");
                var config = new BufferConfig(MetadataSize, PayloadSize);
                using var reader = new Reader(bufferName, config);
                
                // Wait for writer to write metadata
                Log("Reader: Waiting for metadata");
                await Task.Delay(2000, cancellationToken); // Give writer time to connect and write
                
                // Read metadata
                Log("Reader: Reading metadata");
                var metadata = reader.GetMetadata();
                AssertEquals(MetadataTestSize, metadata.Length, "Metadata size mismatch");
                
                // Verify metadata content
                for (int i = 0; i < MetadataTestSize; i++)
                {
                    AssertEquals((byte)i, metadata[i], $"Metadata byte {i} mismatch");
                }
                Log("Reader: Metadata verified");
                
                // Read frame
                Log("Reader: Reading frame");
                var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                AssertTrue(frame.IsValid, "Frame is not valid");
                AssertEquals((ulong)1, frame.Sequence, "Frame sequence number mismatch");
                AssertEquals(FrameTestSize, frame.Size, "Frame size mismatch");
                
                // Verify frame data
                var frameData = frame.ToArray();
                for (int i = 0; i < FrameTestSize; i++)
                {
                    AssertEquals((byte)(i % 256), frameData[i], $"Frame byte {i} mismatch");
                }
                Log("Reader: Frame data verified");
                
                Log("Reader: Test completed successfully");
                return 0;
            }
            catch (Exception ex)
            {
                LogError($"Reader exception: {ex.Message}");
                return 1;
            }
        }
        
        public override async Task<int> RunWriterAsync(string bufferName, CancellationToken cancellationToken)
        {
            try
            {
                // Give reader time to create buffer
                await Task.Delay(1000, cancellationToken);
                
                Log("Writer: Connecting to buffer");
                using var writer = new Writer(bufferName);
                
                // Write metadata
                Log("Writer: Writing metadata");
                var metadata = new byte[MetadataTestSize];
                for (int i = 0; i < MetadataTestSize; i++)
                {
                    metadata[i] = (byte)i;
                }
                writer.SetMetadata(metadata);
                
                // Write frame
                Log("Writer: Writing frame");
                var frameData = new byte[FrameTestSize];
                for (int i = 0; i < FrameTestSize; i++)
                {
                    frameData[i] = (byte)(i % 256);
                }
                writer.WriteFrame(frameData);
                
                Log("Writer: Test completed successfully");
                return 0;
            }
            catch (Exception ex)
            {
                LogError($"Writer exception: {ex.Message}");
                return 1;
            }
        }
    }
}