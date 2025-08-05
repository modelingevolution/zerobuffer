using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Harmony.Configuration;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ZeroBuffer.Harmony.Tests;

public static class HarmonyTestRunner
{
    public static async Task ExecuteScenarioAsync(ScenarioExecution scenario, ILoggerFactory lf, MultiprocessConfiguration config)
    {
        // Execute the scenario
        using var pm = new ProcessManager(config, lf);
        
        var se = new StepExecutor(pm, lf);
        var result = await scenario.RunAsync(se,pm);
        
        // Assert success
        if (!result.Success)
        {
            var logs = string.Join("\n", result.Logs.Select(l => $"[{l.Timestamp:HH:mm:ss.fff}] [{l.Platform}/{l.Process}] {l.Level}: {l.Message}"));
            throw new Xunit.Sdk.XunitException($"Scenario failed: {result.Error}\n\nLogs:\n{logs}");
        }
    }
}