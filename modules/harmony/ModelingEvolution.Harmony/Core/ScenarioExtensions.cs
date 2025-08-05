namespace ModelingEvolution.Harmony.Core;

/// <summary>
/// Extension methods for ScenarioDefinition
/// </summary>
public static class ScenarioExtensions
{
    /// <summary>
    /// Gets all unique processes required by this scenario
    /// </summary>
    public static IEnumerable<string> GetRequiredProcesses(this ScenarioDefinition scenario)
    {
        var processes = new HashSet<string>();
        
        // Add processes from background steps
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
        
        // Add processes from scenario steps
        foreach (var step in scenario.Steps)
        {
            if (!string.IsNullOrEmpty(step.Process))
            {
                processes.Add(step.Process);
            }
        }
        
        return processes.OrderBy(p => p);
    }
    
    /// <summary>
    /// Gets all steps including background steps
    /// </summary>
    public static IEnumerable<StepDefinition> GetAllSteps(this ScenarioDefinition scenario)
    {
        if (scenario.Background != null)
        {
            foreach (var step in scenario.Background.Steps)
            {
                yield return step;
            }
        }
        
        foreach (var step in scenario.Steps)
        {
            yield return step;
        }
    }
}