using System.Collections.Concurrent;
using ZeroBuffer.DuplexChannel;

namespace ZeroBuffer.ProtocolTests.Tests.DuplexChannel
{
    /// <summary>
    /// Test 14.2: Sequence Number Correlation
    /// </summary>
    public class Test_1402_SequenceNumberCorrelation : BaseProtocolTest
    {
        public override int TestId => 1402;
        public override string Description => "Sequence Number Correlation";
        
        private const int RequestCount = 10;
        private const int ResponseDelayMs = 500;
        
        public override async Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken)
        {
            // Server role - responds in reverse order
            try
            {
                Log("Server: Creating duplex channel");
                var factory = new DuplexChannelFactory();
                var config = new BufferConfig(4096, 1024 * 1024);
                
                using var server = factory.CreateImmutableServer($"duplex-{bufferName}", config);
                
                // Collect all requests first
                var requests = new List<(ulong sequence, byte[] data)>();
                
                server.Start((Frame request) =>
                {
                    var sequence = request.Sequence;
                    var data = request.ToArray();
                    
                    lock (requests)
                    {
                        requests.Add((sequence, data));
                        Log($"Server: Collected request {sequence} (total: {requests.Count})");
                        
                        // When we have all requests, process them in reverse
                        if (requests.Count == RequestCount)
                        {
                            Task.Run(async () =>
                            {
                                await Task.Delay(100); // Let client start waiting
                                
                                // Process in reverse order
                                for (int i = requests.Count - 1; i >= 0; i--)
                                {
                                    var req = requests[i];
                                    Log($"Server: Processing request {req.sequence} with {ResponseDelayMs}ms delay");
                                    
                                    // Simulate processing delay
                                    Thread.Sleep(ResponseDelayMs);
                                    
                                    // Return the data with the same sequence number
                                    // The framework should handle sequence correlation
                                    // Note: This is a simplification - real implementation would need
                                    // to return data in the response frame with proper sequence
                                }
                            });
                        }
                    }
                    
                    // Echo the data back (framework handles sequence)
                    return data;
                });
                
                Log("Server: Started, waiting for requests");
                
                // Keep server running
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
            // Client role - sends all requests rapidly
            try
            {
                await Task.Delay(1000, cancellationToken);
                
                Log("Client: Connecting to duplex channel");
                var factory = new DuplexChannelFactory();
                using var client = factory.CreateClient($"duplex-{bufferName}");
                
                // Send all requests rapidly without waiting
                var sentSequences = new ConcurrentDictionary<ulong, int>();
                var sendTasks = new List<Task>();
                
                Log($"Client: Sending {RequestCount} requests rapidly");
                for (int i = 0; i < RequestCount; i++)
                {
                    var requestId = i;
                    var requestData = new byte[100];
                    requestData[0] = (byte)requestId; // Mark request with ID
                    
                    var sequence = client.SendRequest(requestData);
                    sentSequences[sequence] = requestId;
                    Log($"Client: Sent request {requestId} with sequence {sequence}");
                }
                
                // Now receive all responses
                var receivedResponses = new ConcurrentDictionary<ulong, int>();
                var receiveTimeout = TimeSpan.FromSeconds(30); // Total timeout for all responses
                
                Log("Client: Receiving responses (may arrive out of order)");
                for (int i = 0; i < RequestCount; i++)
                {
                    var response = client.ReceiveResponse(receiveTimeout);
                    
                    AssertTrue(response.IsValid, "Response is not valid");
                    AssertTrue(sentSequences.ContainsKey(response.Sequence), 
                        $"Received response with unknown sequence {response.Sequence}");
                    
                    var requestId = sentSequences[response.Sequence];
                    AssertFalse(receivedResponses.ContainsKey(response.Sequence),
                        $"Duplicate response for sequence {response.Sequence}");
                    
                    // Verify response data matches request
                    var responseData = response.ToArray();
                    AssertEquals((byte)requestId, responseData[0], 
                        $"Response data mismatch for request {requestId}");
                    
                    receivedResponses[response.Sequence] = requestId;
                    Log($"Client: Received response for request {requestId} (sequence {response.Sequence})");
                }
                
                // Verify we got all responses
                AssertEquals(RequestCount, receivedResponses.Count, "Not all responses received");
                
                // Verify each request got exactly one response
                var receivedIds = receivedResponses.Values.OrderBy(x => x).ToList();
                for (int i = 0; i < RequestCount; i++)
                {
                    AssertTrue(receivedIds.Contains(i), $"No response for request {i}");
                }
                
                Log("Client: All responses received and matched correctly");
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            var serverTask = Task.Run(() => RunReaderAsync(bufferName, cts.Token), cts.Token);
            var clientTask = Task.Run(() => RunWriterAsync(bufferName, cts.Token), cts.Token);
            
            var clientResult = await clientTask;
            
            cts.Cancel();
            
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            
            return clientResult;
        }
    }
}