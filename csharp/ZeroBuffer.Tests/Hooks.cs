using System;
using BoDi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;
using ZeroBuffer.Tests.Services;


namespace ZeroBuffer.Tests
{
    [Binding]
    public class Hooks
    {
        private readonly IObjectContainer _objectContainer;

        public Hooks(IObjectContainer objectContainer)
        {
            _objectContainer = objectContainer;
        }

        [BeforeScenario]
        public void BeforeScenario()
        {
            // Create a service collection for Microsoft DI
            var services = new ServiceCollection();
            
            // Configure logging
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddConsole();
            });
            
            // Build the service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Register services in SpecFlow's container
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _objectContainer.RegisterInstanceAs<ILoggerFactory>(loggerFactory);
            
            // Create and register the test context
            var testContext = new TestContext();
            _objectContainer.RegisterInstanceAs<ITestContext>(testContext);
            
            // Create and register the buffer naming service
            var bufferNamingService = new BufferNamingService(loggerFactory.CreateLogger<BufferNamingService>());
            _objectContainer.RegisterInstanceAs<IBufferNamingService>(bufferNamingService);
            
            // Register a factory for creating loggers
            //_objectContainer.RegisterFactoryAs<ILogger<BasicCommunicationSteps>>((container) => loggerFactory.CreateLogger<BasicCommunicationSteps>());
            //_objectContainer.RegisterFactoryAs<ILogger<EdgeCasesSteps>>((container) => loggerFactory.CreateLogger<EdgeCasesSteps>());
            
            // Register step definition classes that need to be injected
            // SpecFlow will handle creation of step definition classes, but we need to ensure
            // their dependencies are available
        }
    }
}