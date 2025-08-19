using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZeroBuffer.DuplexChannel;
using ZeroBuffer.Tests.TestHelpers;

namespace ZeroBuffer.Tests
{
    public class DuplexChannelTests : IDisposable
    {
        private readonly string _testChannelName;
        private readonly ITestOutputHelper _output;
        
        public DuplexChannelTests(ITestOutputHelper output)
        {
            _testChannelName = $"test_duplex_{Guid.NewGuid():N}";
            _output = output;
        }
        
        public void Dispose()
        {
            // Cleanup is handled by proper disposal of Reader/Writer
        }
        
        [Fact]
        public void ImmutableServer_EchoTest()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024); // 10MB buffer
            
            // Create server with echo handler
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start((Frame request, Writer responseWriter) =>
            {
                // Echo the request data back
                responseWriter.WriteFrame(request.Span);
            });
            
            // Give server time to initialize
            //Thread.Sleep(100);
            
            // Create client
            using var client = factory.CreateClient(_testChannelName);
            
            // Send test message
            var testData = Encoding.UTF8.GetBytes("Hello, Duplex Channel!");
            var sequenceNumber = client.SendRequest(testData);
            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(testData, response.ToArray());
        }
        
        [Fact]
        public void ImmutableServer_TransformTest()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server that transforms data
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start((Frame request, Writer responseWriter) =>
            {
                var data = request.ToArray();
                // Simple transform: reverse the bytes
                Array.Reverse(data);
                responseWriter.WriteFrame(data);
            });
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            var testData = new byte[] { 1, 2, 3, 4, 5 };
            var sequenceNumber = client.SendRequest(testData);
            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(new byte[] { 5, 4, 3, 2, 1 }, response.ToArray());
        }
        
        // v1.0.0: MutableServer is not supported, will be implemented in v2.0.0
        // This test is commented out until v2.0.0
        /*
        [Fact]
        public void MutableServer_InPlaceTransformTest()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create mutable server that modifies data in-place
            using var server = factory.CreateMutableServer(_testChannelName, config);
            
            server.Start(frame =>
            {
                // Get mutable span for true zero-copy in-place modification
                var span = frame.GetMutableSpan();
                // In-place transform: XOR with 0xFF
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] ^= 0xFF;
                }
            });
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            var testData = new byte[] { 0x00, 0x01, 0xFE, 0xFF };
            var sequenceNumber = client.SendRequest(testData);
            var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(new byte[] { 0xFF, 0xFE, 0x01, 0x00 }, response.ToArray());
        }
        */
        
        
        [Fact]
        public void LatencyMeasurement_Test()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server with minimal processing
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start((Frame request, Writer responseWriter) => responseWriter.WriteFrame(request.Span));
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            // Warm up
            for (int i = 0; i < 10; i++)
            {
                var seq = client.SendRequest(new byte[1024]);
                client.ReceiveResponse(TimeSpan.FromSeconds(1)).Dispose();
            }
            
            // Measure latency
            var latencies = new double[100];
            var testData = new byte[1024];
            
            for (int i = 0; i < latencies.Length; i++)
            {
                var sw = Stopwatch.StartNew();
                var sequenceNumber = client.SendRequest(testData);
                using var response = client.ReceiveResponse(TimeSpan.FromSeconds(1));
                sw.Stop();
                
                Assert.True(response.IsValid);
                Assert.Equal(sequenceNumber, response.Sequence);
                latencies[i] = sw.Elapsed.TotalMilliseconds;
            }
            
            var avgLatency = latencies.Average();
            var minLatency = latencies.Min();
            var maxLatency = latencies.Max();
            
            // Log results
            _output.WriteLine($"Duplex Channel Latency - Avg: {avgLatency:F2}ms, Min: {minLatency:F2}ms, Max: {maxLatency:F2}ms");
            
            // Basic sanity checks
            Assert.True(avgLatency < 50, $"Average latency too high: {avgLatency}ms");
            Assert.True(minLatency < 10, $"Minimum latency too high: {minLatency}ms");
        }
        
        [Fact]
        public void ServerStop_ClientHandlesGracefully()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);

            
            using var server = factory.CreateImmutableServer(_testChannelName, config); // the response buffer is created here. 
            

            server.Start((Frame request, Writer responseWriter) => responseWriter.WriteFrame(request.Span));
            
            using var client = factory.CreateClient(_testChannelName); // the request buffer is created here

            // Verify connection works
            var seq1 = client.SendRequest(new byte[] { 1, 2, 3 });
            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(1));
            Assert.True(response.IsValid);
            Assert.Equal(seq1, response.Sequence);
            
            // Stop server
            server.Stop();
            
            // Client should detect server disconnection
            // SendRequest should throw ReaderDeadException when server's reader is gone
            Assert.Throws<ReaderDeadException>(() => 
            {
                client.SendRequest(new byte[] { 4, 5, 6 });
            });
        }
    }
}