using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZeroBuffer.DuplexChannel;
using ZeroBuffer.Tests.TestHelpers;

namespace ZeroBuffer.Tests
{
    public class DuplexChannelIntegrationTests : IDisposable
    {
        private readonly string _testChannelName;
        private readonly ITestOutputHelper _output;
        
        public DuplexChannelIntegrationTests(ITestOutputHelper output)
        {
            _testChannelName = $"test_duplex_{Guid.NewGuid():N}";
            _output = output;
        }
        
        public void Dispose()
        {
            // Cleanup is handled by proper disposal of Reader/Writer
        }
        
        [Fact]
        public void SimplifiedProtocol_EchoTest()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server that echoes data back
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            server.Start(OnHandle);

            Thread.Sleep(100);
            
            // Create client
            using var client = factory.CreateClient(_testChannelName);
            
            // Send request and get sequence number
            var testData = Encoding.UTF8.GetBytes("Hello, simplified duplex!");
            ulong sequenceNumber = client.SendRequest(testData);
            
            // Receive response
            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            // Verify sequence number matches
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(testData, response.ToArray());
        }
        
        [Fact]
        public void ZeroCopyClient_Test()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            server.Start(OnHandle);
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            // Use zero-copy write
            var testData = "Zero-copy test data";
            var bytes = Encoding.UTF8.GetBytes(testData);
            
            ulong sequenceNumber = client.AcquireRequestBuffer(bytes.Length, out Span<byte> buffer);
            bytes.CopyTo(buffer);
            client.CommitRequest();

            // Receive response
            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid);
            Assert.Equal(sequenceNumber, response.Sequence);
            Assert.Equal(testData, Encoding.UTF8.GetString(response.ToArray()));
        }

        private static ReadOnlySpan<byte> OnHandle(Frame request) => request.Span;

        [Fact]
        public void IndependentSendReceive_Test()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server that adds 1 to each byte
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            server.Start(request =>
            {
                var data = request.ToArray();
                for (int i = 0; i < data.Length; i++)
                    data[i] = (byte)((data[i] + 1) % 256);
                return data;
            });
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            // Send multiple requests from one thread
            var sendTask = Task.Run(() =>
            {
                var sequences = new ulong[10];
                for (int i = 0; i < 10; i++)
                {
                    sequences[i] = client.SendRequest(new byte[] { (byte)i });
                    Thread.Sleep(10);
                }
                return sequences;
            });
            
            // Receive responses from another thread
            var receiveTask = Task.Run(() =>
            {
                var responses = new (ulong sequence, byte value)[10];
                for (int i = 0; i < 10; i++)
                {
                    using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
                    if (response.IsValid)
                    {
                        var data = response.ToArray();
                        responses[i] = (response.Sequence, data[0]);
                    }
                }
                return responses;
            });
            
            // Wait for both tasks
            var sequences = sendTask.Result;
            var responses = receiveTask.Result;
            
            // Verify all responses match their requests
            for (int i = 0; i < 10; i++)
            {
                // Find matching response
                bool found = false;
                for (int j = 0; j < 10; j++)
                {
                    if (responses[j].sequence == sequences[i])
                    {
                        Assert.Equal((byte)((i + 1) % 256), responses[j].value);
                        found = true;
                        break;
                    }
                }
                Assert.True(found, $"Response for sequence {sequences[i]} not found");
            }
        }
        
        [Fact]
        public void ServerPreservesSequenceNumber_Test()
        {
            var factory = TestDuplexChannelFactory.Create(_output);
            var config = new BufferConfig(4096, 10 * 1024 * 1024);
            
            // Create server
            using var server = factory.CreateImmutableServer(_testChannelName, config);
            
            ulong capturedSequence = 0;
            server.Start((Frame request) =>
            {
                // Capture the sequence number from request
                capturedSequence = request.Sequence;
                return new ReadOnlySpan<byte>(new byte[] { 42 });
            });
            
            Thread.Sleep(100);
            
            using var client = factory.CreateClient(_testChannelName);
            
            // Send request
            ulong sentSequence = client.SendRequest(new byte[] { 1 });

            // Receive response
            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(1));
            
            // Verify server saw the same sequence we sent
            Assert.Equal(sentSequence, capturedSequence);
            
            // Verify response has the same sequence
            Assert.Equal(sentSequence, response.Sequence);
        }
    }
}