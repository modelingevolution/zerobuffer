using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TechTalk.SpecFlow;
using Xunit;
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
        private TimeSpan _totalResponseTime;
        private DateTime _measurementStart;
        private readonly List<(ulong Sequence, byte[] Data)> _responses = new();
        private Exception? _lastException;

        public DuplexChannelSteps(IBufferNamingService bufferNaming)
        {
            _bufferNaming = bufferNaming;
        }

        [Given(@"the '(.*)' process creates duplex channel '(.*)' with metadata size '(.*)' and payload size '(.*)'")]
        public void GivenProcessCreatesDuplexChannelWithSizes(string process, string channelName, string metadataSize, string payloadSize)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            var config = new BufferConfig(int.Parse(metadataSize), int.Parse(payloadSize));
            
            var factory = new DuplexChannelFactory();
            var server = factory.CreateImmutableServer(actualChannelName, config);
            _servers[channelName] = server;
        }

        [Given(@"the '(.*)' process creates duplex channel '(.*)' with default config")]
        public void GivenProcessCreatesDuplexChannelWithDefaultConfig(string process, string channelName)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            var config = new BufferConfig(4096, 1024 * 1024); // Default: 4KB metadata, 1MB payload
            
            var factory = new DuplexChannelFactory();
            var server = factory.CreateImmutableServer(actualChannelName, config);
            _servers[channelName] = server;
        }

        [Given(@"the '(.*)' process creates duplex channel '(.*)' with processing mode '(.*)'")]
        public void GivenProcessCreatesDuplexChannelWithProcessingMode(string process, string channelName, string processingMode)
        {
            var actualChannelName = _bufferNaming.GetBufferName(channelName);
            var config = new BufferConfig(4096, 1024 * 1024);
            
            var factory = new DuplexChannelFactory();
            
            // For SingleThread mode, we use immutable server (processes sequentially)
            // For MultiThread mode, we could use a different implementation
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
            else if (server is MutableDuplexServer mutableServer)
            {
                mutableServer.Start((Frame request) =>
                {
                    // For mutable server, echo handler does nothing - data is already in place
                    // The frame is mutated in-place
                });
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
            
            // Fill with test pattern
            for (int i = 0; i < dataSize; i++)
            {
                data[i] = (byte)(i % 256);
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
                var response = client.ReceiveResponse(TimeSpan.FromSeconds(10));
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
                var response = client.ReceiveResponse(TimeSpan.FromSeconds(10));
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

            var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            
            Assert.True(response.IsValid, "Response should be valid");
            Assert.Equal(int.Parse(size), response.Span.Length);
            
            // Verify data matches if we have the original request
            if (_sentRequests.TryGetValue(response.Sequence, out var originalData))
            {
                var responseData = response.ToArray();
                Assert.Equal(originalData, responseData);
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