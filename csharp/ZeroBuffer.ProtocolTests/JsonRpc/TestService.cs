using System.Collections.Concurrent;
using StreamJsonRpc;

namespace ZeroBuffer.ProtocolTests.JsonRpc
{
    /// <summary>
    /// JSON-RPC service for controlling protocol tests
    /// </summary>
    public class TestService
    {
        private readonly ConcurrentDictionary<Guid, TestContext> _activeTests = new();
        private readonly TestRegistry _registry = TestRegistry.Instance;

        public TestService()
        {
            // Register all tests
            TestRegistry.DiscoverAndRegisterAll();
        }

        /// <summary>
        /// Setup a test instance
        /// </summary>
        public TestSetupResponse Setup(TestSetupRequest request)
        {
            var test = _registry.Get(request.TestId);
            if (test == null)
            {
                throw new ArgumentException($"Test {request.TestId} not found");
            }

            var handle = Guid.NewGuid();
            var context = new TestContext(test, request.Role, request.BufferName);
            _activeTests[handle] = context;

            return new TestSetupResponse
            {
                Handle = handle,
                TestId = request.TestId,
                Description = test.Description
            };
        }

        /// <summary>
        /// Execute a test step
        /// </summary>
        public async Task<TestStepResponse> Step(TestStepRequest request)
        {
            if (!_activeTests.TryGetValue(request.Handle, out var context))
            {
                throw new ArgumentException($"Test handle {request.Handle} not found");
            }

            try
            {
                var result = await context.ExecuteStepAsync(request.StepName, request.Args);
                return new TestStepResponse
                {
                    Success = true,
                    Data = result
                };
            }
            catch (Exception ex)
            {
                return new TestStepResponse
                {
                    Success = false,
                    Error = ex.Message,
                    Data = new { exceptionType = ex.GetType().Name }
                };
            }
        }

        /// <summary>
        /// Teardown a test instance
        /// </summary>
        public TestTeardownResponse Teardown(TestTeardownRequest request)
        {
            if (!_activeTests.TryRemove(request.Handle, out var context))
            {
                throw new ArgumentException($"Test handle {request.Handle} not found");
            }

            context.Dispose();

            return new TestTeardownResponse
            {
                ExitCode = context.ExitCode,
                Summary = context.GetSummary()
            };
        }
    }

    // Request/Response DTOs
    public class TestSetupRequest
    {
        public int TestId { get; set; }
        public string Role { get; set; } = "";
        public string BufferName { get; set; } = "";
    }

    public class TestSetupResponse
    {
        public Guid Handle { get; set; }
        public int TestId { get; set; }
        public string Description { get; set; } = "";
    }

    public class TestStepRequest
    {
        public Guid Handle { get; set; }
        public string StepName { get; set; } = "";
        public Dictionary<string, object>? Args { get; set; }
    }

    public class TestStepResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public object? Data { get; set; }
    }

    public class TestTeardownRequest
    {
        public Guid Handle { get; set; }
    }

    public class TestTeardownResponse
    {
        public int ExitCode { get; set; }
        public string Summary { get; set; } = "";
    }
}