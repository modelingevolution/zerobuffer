using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroBuffer.DuplexChannel;

namespace ZeroBuffer.Tests
{
    public class DuplexChannelTests : IDisposable
    {
        private readonly string _testChannelName;
        
        public DuplexChannelTests()
        {
            _testChannelName = $"test_duplex_{Guid.NewGuid():N}";
        }
        
        public void Dispose()
        {
            // Cleanup is handled by proper disposal of Reader/Writer
        }
        
        [Fact]
        public void ImmutableServer_EchoTest()
        {
            var factory = DuplexChannelFactory.Instance;
            var config = new BufferConfig(4096, 10 * 1024 * 1024); // 10MB buffer
            
            // Create server with echo handler
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start(request =>
            {
                // Echo the request data back
                return request.ToArray();
            });
            
            // Give server time to initialize
            Thread.Sleep(100);
            
            // Create client
            using var client = factory.CreateClient(_testChannelName);
            
            // Send test message
            var testData = Encoding.UTF8.GetBytes("Hello, Duplex Channel!");
            var sequenceNumber = client.SendRequest(testData);
            var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(testData, response.ToArray());
        }
        
        [Fact]
        public void ImmutableServer_TransformTest()
        {
            var factory = DuplexChannelFactory.Instance;
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server that transforms data
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start(request =>
            {
                var data = request.ToArray();
                // Simple transform: reverse the bytes
                Array.Reverse(data);
                return data;
            });
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            var testData = new byte[] { 1, 2, 3, 4, 5 };
            var sequenceNumber = client.SendRequest(testData);
            var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(new byte[] { 5, 4, 3, 2, 1 }, response.ToArray());
        }
        
        [Fact]
        public void MutableServer_InPlaceTransformTest()
        {
            var factory = DuplexChannelFactory.Instance;
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
        
        
        [Fact]
        public void MultipleConcurrentRequests_Test()
        {
            var factory = DuplexChannelFactory.Instance;
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server that adds 1 to each byte
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start(request =>
            {
                var data = request.ToArray();
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)((data[i] + 1) % 256);
                }
                Thread.Sleep(10); // Simulate processing time
                return data;
            });
            
            Thread.Sleep(100);
            
            // Run multiple clients concurrently
            const int clientCount = 5;
            const int requestsPerClient = 10;
            var tasks = new Task[clientCount];
            
            for (int clientId = 0; clientId < clientCount; clientId++)
            {
                var id = clientId;
                tasks[clientId] = Task.Run(() =>
                {
                    using var client = factory.CreateClient(_testChannelName);
                    
                    for (int i = 0; i < requestsPerClient; i++)
                    {
                        var testData = new byte[] { (byte)(id * 10 + i) };
                        var sequenceNumber = client.SendRequest(testData);
                        var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
                        
                        Assert.True(response.IsValid);
                        Assert.Equal(sequenceNumber, response.Sequence);
                        var responseData = response.ToArray();
                        Assert.Single(responseData);
                        Assert.Equal((byte)((id * 10 + i + 1) % 256), responseData[0]);
                    }
                });
            }
            
            Task.WaitAll(tasks);
        }
        
        [Fact]
        public void LatencyMeasurement_Test()
        {
            var factory = DuplexChannelFactory.Instance;
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server with minimal processing
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            server.Start(request => request.ToArray());
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            // Warm up
            for (int i = 0; i < 10; i++)
            {
                var seq = client.SendRequest(new byte[1024]);
                client.ReceiveResponse(TimeSpan.FromSeconds(1));
            }
            
            // Measure latency
            var latencies = new double[100];
            var testData = new byte[1024];
            
            for (int i = 0; i < latencies.Length; i++)
            {
                var sw = Stopwatch.StartNew();
                var sequenceNumber = client.SendRequest(testData);
                var response = client.ReceiveResponse(TimeSpan.FromSeconds(1));
                sw.Stop();
                
                Assert.True(response.IsValid);
                Assert.Equal(sequenceNumber, response.Sequence);
                latencies[i] = sw.Elapsed.TotalMilliseconds;
            }
            
            var avgLatency = latencies.Average();
            var minLatency = latencies.Min();
            var maxLatency = latencies.Max();
            
            // Log results
            Console.WriteLine($"Duplex Channel Latency - Avg: {avgLatency:F2}ms, Min: {minLatency:F2}ms, Max: {maxLatency:F2}ms");
            
            // Basic sanity checks
            Assert.True(avgLatency < 50, $"Average latency too high: {avgLatency}ms");
            Assert.True(minLatency < 10, $"Minimum latency too high: {minLatency}ms");
        }
        
        [Fact]
        public void ServerStop_ClientHandlesGracefully()
        {
            var factory = DuplexChannelFactory.Instance;
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            server.Start(request => request.ToArray());
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            // Verify connection works
            var seq1 = client.SendRequest(new byte[] { 1, 2, 3 });
            var response = client.ReceiveResponse(TimeSpan.FromSeconds(1));
            Assert.True(response.IsValid);
            Assert.Equal(seq1, response.Sequence);
            
            // Stop server
            server.Stop();
            Thread.Sleep(100);
            
            // Client request should timeout
            var seq2 = client.SendRequest(new byte[] { 4, 5, 6 });
            response = client.ReceiveResponse(TimeSpan.FromSeconds(1));
            Assert.False(response.IsValid);
        }
    }
}