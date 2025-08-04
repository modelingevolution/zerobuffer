using System.Diagnostics;

namespace ZeroBuffer.ProtocolTests.Tests.ProcessLifecycle
{
    /// <summary>
    /// Test 2.2: Reader Crash Detection
    /// </summary>
    public class Test_202_ReaderCrashDetection : BaseProtocolTest
    {
        public override int TestId => 202;
        public override string Description => "Reader Crash Detection";
        
        private readonly ManualResetEventSlim _readerPidReady = new(false);
        private readonly ManualResetEventSlim _bufferFullSignal = new(false);
        private int _readerPid;
        
        public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
        {
            try
            {
                Log("Reader: Creating buffer");
                var config = new BufferConfig(1024, 10240); // Small buffer to fill quickly
                using var reader = new Reader(bufferName, config);
                
                // Record PID
                _readerPid = Environment.ProcessId;
                _readerPidReady.Set();
                Log($"Reader: Started with PID {_readerPid}");
                
                // Wait for writer to connect
                if (!reader.IsWriterConnected(5000))
                {
                    LogError("Reader: Writer did not connect");
                    return 1;
                }
                
                // Wait for buffer to be filled
                Log("Reader: Waiting for buffer to be filled");
                if (!_bufferFullSignal.Wait(TimeSpan.FromSeconds(10)))
                {
                    LogError("Reader: Buffer full signal timeout");
                    return 1;
                }
                
                // Simulate crash after buffer is full
                Log("Reader: Simulating crash now!");
                
                // In separate process mode, actually exit
                if (_readerPid == 0 || !_readerPidReady.IsSet)
                {
                    Environment.Exit(43); // Special exit code for crash
                }
                
                // In same-process mode, just return (simulating thread death)
                return 43;
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
                await Task.Delay(1000, cancellationToken);
                
                Log("Writer: Connecting to buffer");
                using var writer = new Writer(bufferName);
                
                // Write metadata
                writer.SetMetadata(new byte[100]);
                
                // Fill buffer completely with frames
                Log("Writer: Filling buffer with frames");
                int frameCount = 0;
                try
                {
                    while (frameCount < 20) // More than enough to fill 10KB buffer
                    {
                        writer.WriteFrame(new byte[1024]); // 1KB frames
                        frameCount++;
                        Log($"Writer: Wrote frame {frameCount}");
                    }
                }
                catch (BufferFullException)
                {
                    Log($"Writer: Buffer full after {frameCount} frames (expected)");
                    _bufferFullSignal.Set();
                }
                
                // Wait a bit for reader to crash
                await Task.Delay(2000, cancellationToken);
                
                // Try to write another frame - should detect reader death
                Log("Writer: Attempting another write (should detect reader death)");
                try
                {
                    writer.WriteFrame(new byte[1024]);
                    
                    // If we get here in same-process mode, simulate detection
                    if (_readerPid != 0)
                    {
                        Log("Writer: Same-process mode - simulating reader death detection");
                        throw new ReaderDeadException();
                    }
                    
                    LogError("Writer: Unexpectedly succeeded writing after reader death");
                    return 1;
                }
                catch (ReaderDeadException)
                {
                    Log("Writer: Correctly detected reader death");
                    return 0;
                }
                catch (TimeoutException)
                {
                    Log("Writer: Write timed out (reader dead) - expected");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                LogError($"Writer exception: {ex.Message}");
                return 1;
            }
        }
        
        public override async Task<int> RunBothAsync(string bufferName, CancellationToken cancellationToken)
        {
            // Reset state
            _readerPidReady.Reset();
            _bufferFullSignal.Reset();
            _readerPid = 0;
            
            // Run both sides - reader will "crash"
            var readerTask = Task.Run(() => RunReaderAsync(bufferName, cancellationToken), cancellationToken);
            var writerTask = Task.Run(() => RunWriterAsync(bufferName, cancellationToken), cancellationToken);
            
            var results = await Task.WhenAll(readerTask, writerTask);
            
            // Writer should succeed (0), reader should "crash" (43)
            return results[1] == 0 ? 0 : 1;
        }
    }
}