namespace ZeroBuffer.ProtocolTests.Tests.BasicCommunication
{
    /// <summary>
    /// Test 1.2: Multiple Frames Sequential
    /// </summary>
    public class Test_102_MultipleFramesSequential : BaseProtocolTest
    {
        public override int TestId => 102;
        public override string Description => "Multiple Frames Sequential";
        
        private const int MetadataSize = 1024;      // 1KB
        private const int PayloadSize = 102400;     // 100KB
        private const int MetadataTestSize = 200;   // 200 bytes
        private const int FrameSize = 5120;         // 5KB
        private const int FrameCount = 10;
        
        public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
        {
            try
            {
                Log("Reader: Creating buffer");
                var config = new BufferConfig(MetadataSize, PayloadSize);
                using var reader = new Reader(bufferName, config);
                
                // Wait for writer to connect and write
                Log("Reader: Waiting for writer");
                await Task.Delay(2000, cancellationToken);
                
                // Read metadata
                Log("Reader: Reading metadata");
                var metadata = reader.GetMetadata();
                AssertEquals(MetadataTestSize, metadata.Length, "Metadata size mismatch");
                
                // Read all frames
                for (int i = 0; i < FrameCount; i++)
                {
                    Log($"Reader: Reading frame {i + 1}");
                    var frame = reader.ReadFrame(TimeSpan.FromSeconds(5));
                    
                    AssertTrue(frame.IsValid, $"Frame {i + 1} is not valid");
                    AssertEquals((ulong)(i + 1), frame.Sequence, $"Frame {i + 1} sequence mismatch");
                    AssertEquals(FrameSize, frame.Size, $"Frame {i + 1} size mismatch");
                    
                    // Verify frame content - each frame has a different pattern
                    var data = frame.ToArray();
                    byte expectedFirstByte = (byte)(i * 10);
                    for (int j = 0; j < FrameSize; j++)
                    {
                        AssertEquals((byte)((expectedFirstByte + j) % 256), data[j], 
                            $"Frame {i + 1} byte {j} mismatch");
                    }
                    
                    Log($"Reader: Frame {i + 1} verified");
                }
                
                Log("Reader: All frames received in order");
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
                await Task.Delay(500, cancellationToken);
                
                Log("Writer: Connecting to buffer");
                using var writer = new Writer(bufferName);
                
                // Write metadata
                Log("Writer: Writing metadata");
                var metadata = new byte[MetadataTestSize];
                for (int i = 0; i < MetadataTestSize; i++)
                {
                    metadata[i] = (byte)(i % 256);
                }
                writer.SetMetadata(metadata);
                
                // Write frames
                for (int i = 0; i < FrameCount; i++)
                {
                    Log($"Writer: Writing frame {i + 1}");
                    var frameData = new byte[FrameSize];
                    byte startValue = (byte)(i * 10);
                    
                    for (int j = 0; j < FrameSize; j++)
                    {
                        frameData[j] = (byte)((startValue + j) % 256);
                    }
                    
                    writer.WriteFrame(frameData);
                }
                
                Log("Writer: All frames sent");
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