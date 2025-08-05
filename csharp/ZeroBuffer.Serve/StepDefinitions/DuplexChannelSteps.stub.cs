using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;
using ZeroBuffer;
using ZeroBuffer.Serve.JsonRpc;

namespace ZeroBuffer.Serve.StepDefinitions;

[Binding]
public class DuplexChannelStepsStub
{
    private readonly ITestContext _testContext;
    private readonly ILogger<DuplexChannelStepsStub> _logger;

    public DuplexChannelStepsStub(ITestContext testContext, ILogger<DuplexChannelStepsStub> logger)
    {
        _testContext = testContext;
        _logger = logger;
    }

    [Given(@"the test mode is configured")]
    public void GivenTheTestModeIsConfigured()
    {
        _logger.LogInformation("Test mode configured for duplex channel");
    }

    [Given(@"creates duplex channel '([^']+)' with metadata size '([^']+)' and payload size '([^']+)'")]
    public void GivenCreatesDuplexChannelWithMetadataAndPayloadSize(string channelName, string metadataSize, string payloadSize)
    {
        _logger.LogInformation("Creating duplex channel - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - create duplex channel");
    }

    [Given(@"creates duplex channel '([^']+)' with default config")]
    public void GivenCreatesDuplexChannelWithDefaultConfig(string channelName)
    {
        _logger.LogInformation("Creating duplex channel with default config - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - create duplex channel default");
    }

    [Given(@"starts echo handler")]
    public void GivenStartsEchoHandler()
    {
        _logger.LogInformation("Starting echo handler - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - start echo handler");
    }

    [When(@"creates duplex channel client '([^']+)'")]
    public void WhenCreatesDuplexChannelClient(string channelName)
    {
        _logger.LogInformation("Creating duplex channel client - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - create duplex client");
    }

    [When(@"sends request with size '([^']+)'")]
    public async Task WhenSendsRequestWithSize(string size)
    {
        _logger.LogInformation("Sending request - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - send request");
    }

    [Then(@"response should match request with size '([^']+)'")]
    public void ThenResponseShouldMatchRequestWithSize(string size)
    {
        _logger.LogInformation("Verifying response - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - verify response");
    }

    [Then(@"all responses should have correct sequence numbers")]
    public void ThenAllResponsesShouldHaveCorrectSequenceNumbers()
    {
        _logger.LogInformation("Verifying sequence numbers - stub implementation");
        throw new NotImplementedException("DuplexChannelSteps stub - verify sequence numbers");
    }
}