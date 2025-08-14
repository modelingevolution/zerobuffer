using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Tests
{
    public class DebugFreeSpaceTest : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _bufferName;

        public DebugFreeSpaceTest(ITestOutputHelper output)
        {
            _output = output;
            _bufferName = $"test-debug-{Guid.NewGuid():N}";
        }

        public void Dispose()
        {
            // Cleanup is handled by Reader/Writer dispose
        }

        [Fact]
        public void DebugSimpleWrapScenario()
        {
            _output.WriteLine($"=== C# Debug Test: Buffer {_bufferName} ===");
            
            // Create a small buffer to force wrapping
            var config = new BufferConfig(100, 1000); // 1KB payload
            
            using (var reader = new Reader(_bufferName, config))
            using (var writer = new Writer(_bufferName))
            {
                // Get initial state
                var oieb = GetOIEB(reader);
                _output.WriteLine($"Initial state:");
                _output.WriteLine($"  payload_size: {oieb.PayloadSize}");
                _output.WriteLine($"  payload_free_bytes: {oieb.PayloadFreeBytes}");
                _output.WriteLine($"  payload_write_pos: {oieb.PayloadWritePos}");
                _output.WriteLine($"  payload_read_pos: {oieb.PayloadReadPos}");
                
                // Calculate frame details
                int frameDataSize = 100;
                int frameHeaderSize = Marshal.SizeOf<FrameHeader>();
                int totalFrameSize = frameHeaderSize + frameDataSize;
                _output.WriteLine($"\nFrame details:");
                _output.WriteLine($"  Data size: {frameDataSize}");
                _output.WriteLine($"  Header size: {frameHeaderSize}");
                _output.WriteLine($"  Total frame size: {totalFrameSize}");
                
                // Write frames until we're near the end
                byte[] data = new byte[frameDataSize];
                int framesToWrite = 8;
                _output.WriteLine($"\nWriting {framesToWrite} frames sequentially...");
                
                for (int i = 0; i < framesToWrite; i++)
                {
                    _output.WriteLine($"\n--- Writing frame {i + 1} ---");
                    oieb = GetOIEB(reader);
                    _output.WriteLine($"Before write:");
                    _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                    _output.WriteLine($"  Write pos: {oieb.PayloadWritePos}");
                    _output.WriteLine($"  Space to end: {oieb.PayloadSize - oieb.PayloadWritePos}");
                    
                    writer.WriteFrame(data);
                    
                    oieb = GetOIEB(reader);
                    _output.WriteLine($"After write:");
                    _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                    _output.WriteLine($"  Write pos: {oieb.PayloadWritePos}");
                    _output.WriteLine($"  Written count: {oieb.PayloadWrittenCount}");
                }
                
                _output.WriteLine($"\n=== Reading all {framesToWrite} frames ===");
                
                // Read all frames to free up space
                for (int i = 0; i < framesToWrite; i++)
                {
                    _output.WriteLine($"\n--- Reading frame {i + 1} ---");
                    oieb = GetOIEB(reader);
                    _output.WriteLine($"Before read:");
                    _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                    _output.WriteLine($"  Read pos: {oieb.PayloadReadPos}");
                    
                    using var frame = reader.ReadFrame();
                    
                    oieb = GetOIEB(reader);
                    _output.WriteLine($"After read:");
                    _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                    _output.WriteLine($"  Read pos: {oieb.PayloadReadPos}");
                    _output.WriteLine($"  Read count: {oieb.PayloadReadCount}");
                    _output.WriteLine($"  Frame size: {frame.Size}, sequence: {frame.Sequence}");
                }
                
                // Now write a frame that will cause wrapping
                _output.WriteLine($"\n=== Writing frame that should cause wrap ===");
                oieb = GetOIEB(reader);
                _output.WriteLine($"Current state:");
                _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                _output.WriteLine($"  Write pos: {oieb.PayloadWritePos}");
                _output.WriteLine($"  Read pos: {oieb.PayloadReadPos}");
                _output.WriteLine($"  Space to end: {oieb.PayloadSize - oieb.PayloadWritePos}");
                _output.WriteLine($"  Frame size needed: {totalFrameSize}");
                
                bool shouldWrap = (oieb.PayloadSize - oieb.PayloadWritePos) < (ulong)totalFrameSize;
                _output.WriteLine($"  Should wrap? {shouldWrap}");
                
                writer.WriteFrame(data);
                
                oieb = GetOIEB(reader);
                _output.WriteLine($"\nAfter wrap write:");
                _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                _output.WriteLine($"  Write pos: {oieb.PayloadWritePos}");
                _output.WriteLine($"  Written count: {oieb.PayloadWrittenCount}");
                
                // Read the wrapped frame
                _output.WriteLine($"\n=== Reading wrapped frame ===");
                using var wrappedFrame = reader.ReadFrame();
                
                oieb = GetOIEB(reader);
                _output.WriteLine($"After reading wrapped frame:");
                _output.WriteLine($"  Free bytes: {oieb.PayloadFreeBytes}");
                _output.WriteLine($"  Read pos: {oieb.PayloadReadPos}");
                _output.WriteLine($"  Read count: {oieb.PayloadReadCount}");
                _output.WriteLine($"  Frame size: {wrappedFrame.Size}, sequence: {wrappedFrame.Sequence}");
                
                _output.WriteLine($"\n=== Test Complete ===");
            }
        }
        
        private unsafe OIEB GetOIEB(Reader reader)
        {
            // Use reflection to access the shared memory field from the reader
            var sharedMemoryField = typeof(Reader).GetField("_sharedMemory", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var sharedMemory = sharedMemoryField!.GetValue(reader)!;
            
            // Use reflection to call the Read method
            var readMethod = sharedMemory.GetType().GetMethod("Read", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var genericMethod = readMethod!.MakeGenericMethod(typeof(OIEB));
            return (OIEB)genericMethod.Invoke(sharedMemory, new object[] { 0L })!;
        }
    }
}