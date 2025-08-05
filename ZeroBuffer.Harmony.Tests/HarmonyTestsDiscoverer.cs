using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.Harmony.Configuration;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ZeroBuffer.Harmony.Tests;

public class HarmonyTestsDiscoverer : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        // Configure dependencies
        var parser = new GherkinParser(new ProcessContextExtractor());
        var generator = new ScenarioGenerator(parser);

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configPath = Path.Combine(baseDirectory, "harmony-config.json");

        // Create dummy instances for discovery only
        var config = ConfigurationLoader.Load(configPath);
       
        
        // Generate all scenario combinations
        var scenarios = generator.GenerateScenarios(
            config.FeaturesPath,
            config.Platforms.Keys.ToArray());
        
        foreach (var scenario in scenarios)
        {
            yield return [scenario];
        }
    }
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    
   
}