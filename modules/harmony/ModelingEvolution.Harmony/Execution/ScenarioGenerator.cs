using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ModelingEvolution.Harmony.Execution;

/// <summary>
/// Generates test scenarios with all platform combinations
/// </summary>
public interface IScenarioGenerator
{
    IEnumerable<ScenarioExecution> GenerateScenarios(
        string featuresPath,
        params string[] platforms);
}

public class ScenarioGenerator(IGherkinParser parser) : IScenarioGenerator
{
    private readonly IGherkinParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));

    public IEnumerable<ScenarioExecution> GenerateScenarios(
        string featuresPath,
        params string[] platforms)
    {
        var scenarios = _parser.ParseFeatureFiles(Path.Combine(featuresPath, "*.feature"), FeatureIdMapper.GetFeatureId);
        
        foreach (var scenario in scenarios)
        {
            var processNames = ExtractProcessNames(scenario);
            
            if (processNames.Count == 0)
            {
                // No processes mentioned, skip
                continue;
            }
            
            var combinations = GeneratePlatformCombinations(platforms, processNames);
            
            foreach (var combination in combinations)
            {
                yield return new ScenarioExecution(
                    scenario,
                    combination);
            }
        }
    }
    
    private List<string> ExtractProcessNames(ScenarioDefinition scenario)
    {
        var processes = new HashSet<string>();
        
        // Extract from background
        if (scenario.Background != null)
        {
            foreach (var step in scenario.Background.Steps)
            {
                if (!string.IsNullOrEmpty(step.Process))
                {
                    processes.Add(step.Process);
                }
            }
        }
        
        // Extract from scenario steps
        foreach (var step in scenario.Steps)
        {
            if (!string.IsNullOrEmpty(step.Process))
            {
                processes.Add(step.Process);
            }
        }
        
        return processes.OrderBy(p => p).ToList(); // Consistent ordering
    }
    
    private IEnumerable<PlatformCombination> GeneratePlatformCombinations(
        string[] platforms,
        List<string> processNames)
    {
        var combinations = new List<Dictionary<string, string>>();
        GenerateCombinationsRecursive(platforms, processNames, 0, new Dictionary<string, string>(), combinations);
        
        foreach (var combination in combinations)
        {
            yield return new PlatformCombination(combination);
        }
    }
    
    private void GenerateCombinationsRecursive(
        string[] platforms,
        List<string> processNames,
        int processIndex,
        Dictionary<string, string> current,
        List<Dictionary<string, string>> results)
    {
        if (processIndex >= processNames.Count)
        {
            results.Add(new Dictionary<string, string>(current));
            return;
        }
        
        var processName = processNames[processIndex];
        
        foreach (var platform in platforms)
        {
            current[processName] = platform;
            GenerateCombinationsRecursive(platforms, processNames, processIndex + 1, current, results);
        }
    }
}