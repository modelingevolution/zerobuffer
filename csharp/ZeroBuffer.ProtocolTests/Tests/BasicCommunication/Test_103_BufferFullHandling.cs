using System.Diagnostics;

namespace ZeroBuffer.ProtocolTests.Tests.BasicCommunication
{
    /// <summary>
    /// Test 1.3: Buffer Full Handling
    /// </summary>
    public class Test_103_BufferFullHandling : BaseProtocolTest
    {
        public override int TestId => 103;
        public override string Description => "Buffer Full Handling";
        
        private const int MetadataSize = 512;     // 512B
        private const int PayloadSize = 10240;    // 10KB
        private const int FirstFrameSize = 8192;  // 8KB
        private const int SecondFrameSize = 5120; // 5KB
        
        private readonly ManualResetEventSlim _writerBlockedEvent = new(false);
        private readonly ManualResetEventSlim _readerReadyEvent = new(false);
        
        public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
        {
            try
            {
                Log("Reader: Creating buffer");
                var config = new BufferConfig(MetadataSize, PayloadSize);
                using var reader = new Reader(bufferName, config);
                
                // Wait for writer to connect
                Log("Reader: Waiting for writer");
                await Task.Delay(2000, cancellationToken);
                
                // Signal reader is ready
                _readerReadyEvent.Set();
                
                // Wait for writer to be blocked
                Log("Reader: Waiting for writer to block on second write");
                if (!_writerBlockedEvent.Wait(TimeSpan.FromSeconds(5)))
                {
                    LogError("Reader: Writer did not signal it was blocked");
                    return 1;
                }
                
                // Read first frame
                Log("Reader: Reading first frame to free space");
                var frame1 = reader.ReadFrame(TimeSpan.FromSeconds(5));
                AssertTrue(frame1.IsValid, "First frame is not valid");
                AssertEquals(FirstFrameSize, frame1.Size, "First frame size mismatch");
                
                Log("Reader: First frame read, writer should unblock");
                
                // Read second frame (should be available after writer unblocks)
                Log("Reader: Reading second frame");
                var frame2 = reader.ReadFrame(TimeSpan.FromSeconds(10));
                AssertTrue(frame2.IsValid, "Second frame is not valid");
                AssertEquals(SecondFrameSize, frame2.Size, "Second frame size mismatch");
                
                Log("Reader: Both frames received successfully");
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
                writer.SetMetadata(new byte[100]);
                
                // Wait for reader to be ready
                if (!_readerReadyEvent.Wait(TimeSpan.FromSeconds(5)))
                {
                    LogError("Writer: Reader did not signal ready");
                    return 1;
                }
                
                // Write first frame (should succeed)
                Log("Writer: Writing first frame (8KB)");
                var frame1 = new byte[FirstFrameSize];
                writer.WriteFrame(frame1);
                Log("Writer: First frame written successfully");
                
                // Try to write second frame (should block)
                Log("Writer: Attempting to write second frame (5KB) - should block");
                var writeTask = Task.Run(() =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    writer.WriteFrame(new byte[SecondFrameSize]);
                    stopwatch.Stop();
                    return stopwatch.ElapsedMilliseconds;
                });
                
                // Wait a bit to ensure write is blocked
                await Task.Delay(1000, cancellationToken);
                AssertFalse(writeTask.IsCompleted, "Second write should be blocked");
                
                // Signal that we're blocked
                _writerBlockedEvent.Set();
                Log("Writer: Confirmed blocked on second write");
                
                // Wait for write to complete (reader will free space)
                var blockDuration = await writeTask;
                Log($"Writer: Second write completed after {blockDuration}ms");
                AssertTrue(blockDuration >= 900, "Write should have been blocked for at least 900ms");
                
                Log("Writer: Test completed successfully");
                return 0;
            }
            catch (Exception ex)
            {
                LogError($"Writer exception: {ex.Message}");
                return 1;
            }
        }
        
        public override async Task<int> RunBothAsync(string bufferName, CancellationToken cancellationToken)
        {
            // Reset events for same-process mode
            _writerBlockedEvent.Reset();
            _readerReadyEvent.Reset();
            
            return await base.RunBothAsync(bufferName, cancellationToken);
        }
    }
}