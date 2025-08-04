using ZeroBuffer.DuplexChannel;

namespace ZeroBuffer.ProtocolTests.Tests.DuplexChannel
{
    /// <summary>
    /// Test 14.1: Basic Request-Response
    /// </summary>
    public class Test_1401_BasicRequestResponse : BaseProtocolTest
    {
        public override int TestId => 1401;
        public override string Description => "Duplex Channel Basic Request-Response";
        
        private readonly int[] _testSizes = { 1, 1024, 100 * 1024 }; // 1B, 1KB, 100KB
        
        public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
        {
            // In duplex tests, "reader" acts as the server
            try
            {
                Log("Server: Creating duplex channel");
                var factory = new DuplexChannelFactory();
                var config = new BufferConfig(4096, 1024 * 1024); // 4KB metadata, 1MB payload
                
                using var server = factory.CreateImmutableServer($"duplex-{bufferName}", config);
                
                // Echo handler - returns exact request data
                server.Start((Frame request) =>
                {
                    Log($"Server: Received request with sequence {request.Sequence}, size {request.Size}");
                    
                    // Echo back the exact data
                    var data = request.ToArray();
                    return data;
                });
                
                Log("Server: Started, waiting for requests");
                
                // Keep server running for test duration
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Log("Server: Shutting down");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                LogError($"Server exception: {ex.Message}");
                return 1;
            }
        }
        
        public override async Task<int> RunWriterAsync(string bufferName, CancellationToken cancellationToken)
        {
            // In duplex tests, "writer" acts as the client
            try
            {
                // Give server time to start
                await Task.Delay(1000, cancellationToken);
                
                Log("Client: Connecting to duplex channel");
                var factory = new DuplexChannelFactory();
                using var client = factory.CreateClient($"duplex-{bufferName}");
                
                // Track all sent requests
                var sentRequests = new Dictionary<ulong, byte[]>();
                
                // Send requests of different sizes
                foreach (var size in _testSizes)
                {
                    Log($"Client: Sending request of size {size}");
                    
                    // Create test data
                    var requestData = new byte[size];
                    for (int i = 0; i < size; i++)
                    {
                        requestData[i] = (byte)(i % 256);
                    }
                    
                    // Send request
                    var sequence = client.SendRequest(requestData);
                    sentRequests[sequence] = requestData;
                    Log($"Client: Sent request with sequence {sequence}");
                }
                
                // Receive all responses
                var receivedResponses = new HashSet<ulong>();
                for (int i = 0; i < _testSizes.Length; i++)
                {
                    Log("Client: Waiting for response");
                    var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
                    
                    AssertTrue(response.IsValid, "Response is not valid");
                    AssertTrue(sentRequests.ContainsKey(response.Sequence), 
                        $"Received unexpected sequence {response.Sequence}");
                    AssertFalse(receivedResponses.Contains(response.Sequence),
                        $"Received duplicate response for sequence {response.Sequence}");
                    
                    // Verify response matches request
                    var expectedData = sentRequests[response.Sequence];
                    var actualData = response.ToArray();
                    
                    AssertEquals(expectedData.Length, actualData.Length, 
                        $"Response size mismatch for sequence {response.Sequence}");
                    
                    for (int j = 0; j < expectedData.Length; j++)
                    {
                        AssertEquals(expectedData[j], actualData[j], 
                            $"Response data mismatch at byte {j} for sequence {response.Sequence}");
                    }
                    
                    receivedResponses.Add(response.Sequence);
                    Log($"Client: Verified response for sequence {response.Sequence}");
                }
                
                Log("Client: All responses received and verified");
                return 0;
            }
            catch (Exception ex)
            {
                LogError($"Client exception: {ex.Message}");
                return 1;
            }
        }
        
        public override async Task<int> RunBothAsync(string bufferName, CancellationToken cancellationToken)
        {
            // For duplex tests, we need proper cancellation handling
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            var serverTask = Task.Run(() => RunReaderAsync(bufferName, cts.Token), cts.Token);
            var clientTask = Task.Run(() => RunWriterAsync(bufferName, cts.Token), cts.Token);
            
            // Wait for client to complete
            var clientResult = await clientTask;
            
            // Cancel server
            cts.Cancel();
            
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            
            return clientResult; // Return client result as overall result
        }
    }
}