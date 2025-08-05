using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using ModelingEvolution.Harmony.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace ZeroBuffer.Harmony.Tests;

public class ZeroBufferE2ETests
{
    private ILoggerFactory _lf;
    private MultiprocessConfiguration _config;
    public ZeroBufferE2ETests(ITestOutputHelper h)
    {
        _lf = new LoggerFactory().AddXunit(h);
        _config = ConfigurationLoader.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "harmony-config.json"));
    }

    [Theory( DisplayName = "Scenarios")]
    [ClassData(typeof(HarmonyTestsDiscoverer))]
    public async Task RunScenario(ScenarioExecution scenario) 
        => await HarmonyTestRunner.ExecuteScenarioAsync(scenario, _lf,_config);
}