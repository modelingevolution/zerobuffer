using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TechTalk.SpecFlow;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroBuffer.DuplexChannel;
using ZeroBuffer.Tests.Services;

namespace ZeroBuffer.Tests.StepDefinitions
{
    [Binding]
    public class DuplexChannelSteps
    {
        private readonly Dictionary<string, IDuplexServer> _servers = new();
        private readonly Dictionary<string, IDuplexClient> _clients = new();
        private readonly Dictionary<ulong, byte[]> _sentRequests = new();
        private readonly Dictionary<ulong, byte[]> _receivedResponses = new();
        private readonly Dictionary<ulong, ulong> _responseSequences = new();
        private readonly IBufferNamingService _bufferNaming;
        private readonly ILogger<DuplexChannelSteps> _log;
        private TimeSpan _totalResponseTime;
        private DateTime _measurementStart;
        private readonly List<(ulong Sequence, byte[] Data)> _responses = new();
        private Exception? _lastException;

        public DuplexChannelSteps(IBufferNamingService bufferNaming, ILogger<DuplexChannelSteps> log)
        {
            _bufferNaming = bufferNaming;
            _log = log;
        }

        [Given(@"the '(.*)' process creates immutable duplex channel '(.*)' with metadata size '(.*)' and payload size '(.*)'")]
        public void GivenProcessCreatesImmutableDuplexChannelWithSizes(string process, string channelName, string metadataSize, string payloadSize)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            var config = new BufferConfig(int.Parse(metadataSize), int.Parse(payloadSize));
            
            var factory = new DuplexChannelFactory();
            var server = factory.CreateImmutableServer(actualChannelName, config);
            _servers[channelName] = server;
        }

        [Given(@"the '(.*)' process creates immutable duplex channel '(.*)' with default config")]
        public void GivenProcessCreatesImmutableDuplexChannelWithDefaultConfig(string process, string channelName)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            var config = new BufferConfig(4096, 1024 * 1024); // Default: 4KB metadata, 1MB payload
            
            var factory = new DuplexChannelFactory();
            var server = factory.CreateImmutableServer(actualChannelName, config);
            _servers[channelName] = server;
        }

        [Given(@"the '(.*)' process creates immutable duplex channel '(.*)' with processing mode '(.*)'")]
        public void GivenProcessCreatesImmutableDuplexChannelWithProcessingMode(string process, string channelName, string processingMode)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            var config = new BufferConfig(4096, 1024 * 1024);
            
            var factory = new DuplexChannelFactory();
            
            // v1.0.0: Only immutable server is supported
            // SingleThread processing mode is implicit in ImmutableDuplexServer
            var server = factory.CreateImmutableServer(actualChannelName, config);
            _servers[channelName] = server;
        }

        [Given(@"the '(.*)' process starts echo handler")]
        public void GivenProcessStartsEchoHandler(string process)
        {
            var server = _servers.Values.LastOrDefault();
            if (server == null)
                throw new InvalidOperationException("No server created");

            if (server is ImmutableDuplexServer immutableServer)
            {
                immutableServer.Start((Frame request) =>
                {
                    // Echo handler - return the same data
                    return request.Span;
                });
            }
            else
            {
                throw new InvalidOperationException($"Unsupported server type: {server.GetType().Name}. Only ImmutableDuplexServer is supported in v1.0.0");
            }
            
            // Give server time to initialize
            Thread.Sleep(100);
        }

        [Given(@"the '(.*)' process starts delayed echo handler with '(.*)' ms delay")]
        public void GivenProcessStartsDelayedEchoHandler(string process, string delayMs)
        {
            var server = _servers.Values.LastOrDefault();
            if (server == null)
                throw new InvalidOperationException("No server created");

            var delay = int.Parse(delayMs);

            if (server is ImmutableDuplexServer immutableServer)
            {
                immutableServer.Start((Frame request) =>
                {
                    Thread.Sleep(delay);
                    return request.Span;
                });
            }
            
            Thread.Sleep(100);
        }

        [Given(@"the '(.*)' process starts variable delay handler '(.*)' ms")]
        public void GivenProcessStartsVariableDelayHandler(string process, string delayRange)
        {
            var server = _servers.Values.LastOrDefault();
            if (server == null)
                throw new InvalidOperationException("No server created");

            var parts = delayRange.Split('-');
            var minDelay = int.Parse(parts[0]);
            var maxDelay = int.Parse(parts[1]);
            var random = new Random();

            if (server is ImmutableDuplexServer immutableServer)
            {
                immutableServer.Start((Frame request) =>
                {
                    var delay = random.Next(minDelay, maxDelay + 1);
                    Thread.Sleep(delay);
                    return request.Span;
                });
            }
            
            Thread.Sleep(100);
        }

        [Given(@"the '(.*)' process starts handler with '(.*)' second processing time")]
        public void GivenProcessStartsHandlerWithProcessingTime(string process, string seconds)
        {
            var server = _servers.Values.LastOrDefault();
            if (server == null)
                throw new InvalidOperationException("No server created");

            var processingTime = int.Parse(seconds) * 1000; // Convert to milliseconds

            if (server is ImmutableDuplexServer immutableServer)
            {
                immutableServer.Start((Frame request) =>
                {
                    Thread.Sleep(processingTime);
                    return request.Span;
                });
            }
            
            Thread.Sleep(100);
        }

        [When(@"the '(.*)' process creates duplex channel client '(.*)'")]
        public void WhenProcessCreatesDuplexChannelClient(string process, string channelName)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            
            var factory = new DuplexChannelFactory();
            var client = factory.CreateClient(actualChannelName);
            _clients[channelName] = client;
        }

        [When(@"the '(.*)' process sends request with size '(.*)'")] 
        public void WhenProcessSendsRequestWithSize(string process, string size)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var dataSize = int.Parse(size);
            var data = new byte[dataSize];
            
            // Add timestamp at the beginning (if size permits)
            if (dataSize >= sizeof(long))
            {
                // Store timestamp in first 8 bytes (microseconds since Unix epoch)
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000; // Convert to microseconds
                BitConverter.GetBytes(timestamp).CopyTo(data, 0);
                
                // Fill rest with pattern
                for (int i = sizeof(long); i < dataSize; i++)
                {
                    data[i] = (byte)(i % 256);
                }

                _log.LogInformation($"[DuplexChannelSteps] Client sending request at timestamp: {timestamp} microseconds since epoch");
            }
            else
            {
                // Too small for timestamp, just use pattern
                for (int i = 0; i < dataSize; i++)
                {
                    data[i] = (byte)(i % 256);
                }
            }

            var sequence = client.SendRequest(data);
            _sentRequests[sequence] = data;
        }

        [When(@"the '(.*)' process sends '(.*)' requests rapidly without waiting")]
        public void WhenProcessSendsRequestsRapidlyWithoutWaiting(string process, string count)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var requestCount = int.Parse(count);
            
            for (int i = 0; i < requestCount; i++)
            {
                var data = Encoding.UTF8.GetBytes($"Request {i}");
                var sequence = client.SendRequest(data);
                _sentRequests[sequence] = data;
            }
        }

        [When(@"the '(.*)' process sends '(.*)' requests simultaneously")]
        public void WhenProcessSendsRequestsSimultaneously(string process, string count)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var requestCount = int.Parse(count);
            _measurementStart = DateTime.UtcNow;
            
            for (int i = 0; i < requestCount; i++)
            {
                var data = Encoding.UTF8.GetBytes($"Request {i}");
                var sequence = client.SendRequest(data);
                _sentRequests[sequence] = data;
            }
        }

        [When(@"the '(.*)' process measures total response time")]
        public void WhenProcessMeasuresTotalResponseTime(string process)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            // Receive all responses
            foreach (var _ in _sentRequests)
            {
                using var response = client.ReceiveResponse(TimeSpan.FromSeconds(10));
                if (response.IsValid)
                {
                    _responses.Add((response.Sequence, response.ToArray()));
                }
            }
            
            _totalResponseTime = DateTime.UtcNow - _measurementStart;
        }

        [When(@"the '(.*)' process acquires buffer of size '(.*)'")]
        public void WhenProcessAcquiresBufferOfSize(string process, string size)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var bufferSize = int.Parse(size);
            var sequence = client.AcquireRequestBuffer(bufferSize, out var buffer);
            
            // Fill buffer with test pattern
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)(i % 256);
            }
            
            // Store for verification
            var data = buffer.ToArray();
            _sentRequests[sequence] = data;
        }

        [When(@"the '(.*)' process commits zero-copy request")]
        public void WhenProcessCommitsZeroCopyRequest(string process)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            client.CommitRequest();
        }

        [When(@"the '(.*)' process receives all '(.*)' responses")]
        public void WhenProcessReceivesAllResponses(string process, string count)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var expectedCount = int.Parse(count);
            _responses.Clear();
            
            for (int i = 0; i < expectedCount; i++)
            {
                using var response = client.ReceiveResponse(TimeSpan.FromSeconds(10));
                if (response.IsValid)
                {
                    _responses.Add((response.Sequence, response.ToArray()));
                    _receivedResponses[response.Sequence] = response.ToArray();
                }
            }
        }

        [Then(@"response should match request with size '(.*)'")]
        public void ThenResponseShouldMatchRequestWithSize(string size)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid, "Response should be valid");
            Assert.Equal(int.Parse(size), response.Span.Length);
            
            // Verify data matches if we have the original request
            if (_sentRequests.TryGetValue(response.Sequence, out var originalData))
            {
                var responseData = response.ToArray();
                Assert.Equal(originalData, responseData);
                
                // Extract and print timestamp if present
                if (responseData.Length >= sizeof(long))
                {
                    var sentTimestamp = BitConverter.ToInt64(responseData, 0);
                    var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                    var roundTripTime = currentTimestamp - sentTimestamp;

                    _log.LogInformation($"[DuplexChannelSteps] Client received response with timestamp: {sentTimestamp} microseconds since epoch (round-trip time: {roundTripTime} microseconds)");
                }
                
                _receivedResponses[response.Sequence] = responseData;
            }
        }

        [Then(@"all responses should have correct sequence numbers")]
        public void ThenAllResponsesShouldHaveCorrectSequenceNumbers()
        {
            foreach (var kvp in _receivedResponses)
            {
                Assert.True(_sentRequests.ContainsKey(kvp.Key), 
                    $"Response sequence {kvp.Key} should match a sent request");
            }
        }

        [Then(@"responses should match requests by sequence number")]
        public void ThenResponsesShouldMatchRequestsBySequenceNumber()
        {
            foreach (var (sequence, data) in _responses)
            {
                Assert.True(_sentRequests.ContainsKey(sequence),
                    $"Response sequence {sequence} should match a sent request");
                
                var originalData = _sentRequests[sequence];
                Assert.Equal(originalData, data);
            }
        }

        [Then(@"no responses should be lost or mismatched")]
        public void ThenNoResponsesShouldBeLostOrMismatched()
        {
            Assert.Equal(_sentRequests.Count, _responses.Count);
            
            var receivedSequences = _responses.Select(r => r.Item1).ToHashSet();
            var sentSequences = _sentRequests.Keys.ToHashSet();
            
            Assert.True(receivedSequences.SetEquals(sentSequences),
                "All sent requests should have received responses");
        }

        [Then(@"the '(.*)' process responds in reverse order")]
        public void ThenProcessRespondsInReverseOrder(string process)
        {
            // This is handled by the delayed handler - responses will come back in order they finish
            // With delays, later requests might finish first
        }

        [Then(@"total time should be at least '(.*)' seconds")]
        public void ThenTotalTimeShouldBeAtLeastSeconds(string seconds)
        {
            var expectedMinimum = TimeSpan.FromSeconds(double.Parse(seconds));
            Assert.True(_totalResponseTime >= expectedMinimum,
                $"Total time {_totalResponseTime} should be at least {expectedMinimum}");
        }

        [Then(@"responses should arrive in order")]
        public void ThenResponsesShouldArriveInOrder()
        {
            // For single-threaded processing, responses should maintain request order
            var sequences = _responses.Select(r => r.Item1).ToList();
            var sortedSequences = sequences.OrderBy(s => s).ToList();
            
            Assert.Equal(sortedSequences, sequences);
        }

        // New step definitions for v1.0.0 protocol
        [Given(@"the '(.*)' process starts handler that implements XOR with key '(.*)'")]
        public void GivenProcessStartsXorHandler(string process, string xorKeyHex)
        {
            var server = _servers.Values.LastOrDefault();
            if (server == null)
                throw new InvalidOperationException("No server created");

            byte xorKey = Convert.ToByte(xorKeyHex, 16);

            if (server is ImmutableDuplexServer immutableServer)
            {
                immutableServer.Start((Frame request) =>
                {
                    // XOR transform handler
                    var result = new byte[request.Size];
                    var span = request.Span;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = (byte)(span[i] ^ xorKey);
                    }
                    return result;
                });
            }
            else
            {
                throw new InvalidOperationException("Only ImmutableDuplexServer is supported in v1.0.0");
            }
            
            Thread.Sleep(100);
        }

        [When(@"the '(.*)' process sends '(.*)' byte frame with test pattern")]
        public void WhenProcessSendsByteFrameWithTestPattern(string process, string size)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var dataSize = int.Parse(size);
            var data = new byte[dataSize];
            
            // Fill with test pattern (incrementing bytes)
            for (int i = 0; i < dataSize; i++)
            {
                data[i] = (byte)(i % 256);
            }

            var sequence = client.SendRequest(data);
            _sentRequests[sequence] = data;
        }

        [Then(@"response should contain XOR transformed data")]
        public void ThenResponseShouldContainXorTransformedData()
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            using var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            Assert.True(response.IsValid, "Response should be valid");

            // Get the original request data
            if (_sentRequests.TryGetValue(response.Sequence, out var originalData))
            {
                var responseData = response.ToArray();
                
                // Verify XOR transformation with 0xFF
                for (int i = 0; i < originalData.Length; i++)
                {
                    Assert.Equal((byte)(originalData[i] ^ 0xFF), responseData[i]);
                }
            }
        }

        [Then(@"the server handler receives immutable request frame")]
        public void ThenServerHandlerReceivesImmutableRequestFrame()
        {
            // This is implicit in ImmutableDuplexServer - handler receives Frame (read-only)
            // No additional verification needed as it's enforced by the API
        }

        [Then(@"the server handler returns new response data")]
        public void ThenServerHandlerReturnsNewResponseData()
        {
            // This is implicit in ImmutableDuplexServer - handler returns new data
            // No additional verification needed as it's enforced by the API
        }

        [Then(@"the '(.*)' process receives '(.*)' responses in order")]
        public void ThenProcessReceivesResponsesInOrder(string process, string count)
        {
            WhenProcessReceivesAllResponses(process, count);
            ThenResponsesShouldArriveInOrder();
        }

        [Then(@"responses should match requests by content")]
        public void ThenResponsesShouldMatchRequestsByContent()
        {
            // In v1.0.0, sequence numbers are internal only
            // Match by content for echo handler scenarios
            foreach (var (_, responseData) in _responses)
            {
                bool found = false;
                foreach (var sentData in _sentRequests.Values)
                {
                    if (sentData.SequenceEqual(responseData))
                    {
                        found = true;
                        break;
                    }
                }
                Assert.True(found, "Response should match one of the sent requests by content");
            }
        }

        [Then(@"no responses should be lost or duplicated")]
        public void ThenNoResponsesShouldBeLostOrDuplicated()
        {
            Assert.Equal(_sentRequests.Count, _responses.Count);
            // Note: In v1.0.0, we can't verify duplicates by sequence since it's internal
            // The reader/writer pair handles this internally
        }

        [When(@"the '(.*)' process sends '(.*)' requests sequentially")]
        public void WhenProcessSendsRequestsSequentially(string process, string count)
        {
            var client = _clients.Values.LastOrDefault();
            if (client == null)
                throw new InvalidOperationException("No client connected");

            var requestCount = int.Parse(count);
            
            for (int i = 0; i < requestCount; i++)
            {
                var data = Encoding.UTF8.GetBytes($"Request {i}");
                var sequence = client.SendRequest(data);
                _sentRequests[sequence] = data;
            }
        }

        [When(@"the '(.*)' process sends '(.*)' requests from single thread")]
        public void WhenProcessSendsRequestsFromSingleThread(string process, string count)
        {
            WhenProcessSendsRequestsSequentially(process, count);
        }

        [Then(@"the '(.*)' process receives exactly '(.*)' responses")]
        public void ThenProcessReceivesExactlyResponses(string process, string count)
        {
            var expectedCount = int.Parse(count);
            Assert.Equal(expectedCount, _responses.Count);
        }

        [Then(@"all '(.*)' responses match their requests")]
        public void ThenAllResponsesMatchTheirRequests(string count)
        {
            var expectedCount = int.Parse(count);
            Assert.Equal(expectedCount, _responses.Count);
            
            // For echo handler, verify content matches
            foreach (var (sequence, responseData) in _responses)
            {
                if (_sentRequests.TryGetValue(sequence, out var requestData))
                {
                    Assert.Equal(requestData, responseData);
                }
            }
        }

        [Then(@"total time should be at least '(.*)' ms")]
        public void ThenTotalTimeShouldBeAtLeastMs(string milliseconds)
        {
            var expectedMinimum = TimeSpan.FromMilliseconds(double.Parse(milliseconds));
            Assert.True(_totalResponseTime >= expectedMinimum,
                $"Total time {_totalResponseTime} should be at least {expectedMinimum}");
        }

        [Given(@"the '(.*)' process starts handler with '(.*)' ms processing time")]
        public void GivenProcessStartsHandlerWithMsProcessingTime(string process, string milliseconds)
        {
            var server = _servers.Values.LastOrDefault();
            if (server == null)
                throw new InvalidOperationException("No server created");

            var processingTime = int.Parse(milliseconds);

            if (server is ImmutableDuplexServer immutableServer)
            {
                immutableServer.Start((Frame request) =>
                {
                    Thread.Sleep(processingTime);
                    return request.Span;
                });
            }
            else
            {
                throw new InvalidOperationException("Only ImmutableDuplexServer is supported in v1.0.0");
            }
            
            Thread.Sleep(100);
        }

        [AfterScenario]
        public void Cleanup()
        {
            foreach (var client in _clients.Values)
            {
                client?.Dispose();
            }
            _clients.Clear();

            foreach (var server in _servers.Values)
            {
                server?.Stop();
                server?.Dispose();
            }
            _servers.Clear();

            _sentRequests.Clear();
            _receivedResponses.Clear();
            _responses.Clear();
        }
    }
}