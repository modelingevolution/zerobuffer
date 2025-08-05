using Gherkin;
using Gherkin.Ast;
using ModelingEvolution.Harmony.Core;

namespace ModelingEvolution.Harmony.Gherkin;

/// <summary>
/// Parses Gherkin feature files into scenario definitions
/// </summary>
public interface IGherkinParser
{
    IEnumerable<ScenarioDefinition> ParseFeatureFile(string path);
    IEnumerable<ScenarioDefinition> ParseFeatureFiles(string pattern);
}

public class GherkinParser : IGherkinParser
{
    private readonly IProcessContextExtractor _contextExtractor;
    private readonly Parser _parser;
    
    public GherkinParser(IProcessContextExtractor contextExtractor)
    {
        _contextExtractor = contextExtractor ?? throw new ArgumentNullException(nameof(contextExtractor));
        _parser = new Parser();
    }
    
    public IEnumerable<ScenarioDefinition> ParseFeatureFile(string path)
    {
        var document = _parser.Parse(path);
        var feature = document.Feature;
        
        if (feature == null)
        {
            yield break;
        }
        
        BackgroundDefinition? background = null;
        
        foreach (var child in feature.Children)
        {
            if (child is Background bg)
            {
                background = ParseBackground(bg);
            }
            else if (child is Scenario scenario)
            {
                yield return ParseScenario(scenario, background);
            }
            // Skip ScenarioOutline for now - not supported in newer Gherkin versions
        }
    }
    
    public IEnumerable<ScenarioDefinition> ParseFeatureFiles(string pattern)
    {
        var files = Directory.GetFiles(
            Path.GetDirectoryName(pattern) ?? ".",
            Path.GetFileName(pattern),
            SearchOption.AllDirectories
        );
        
        foreach (var file in files)
        {
            foreach (var scenario in ParseFeatureFile(file))
            {
                yield return scenario;
            }
        }
    }
    
    private BackgroundDefinition ParseBackground(Background background)
    {
        return new BackgroundDefinition
        {
            Steps = background.Steps.Select(ParseStep).ToList()
        };
    }
    
    private ScenarioDefinition ParseScenario(Scenario scenario, BackgroundDefinition? background)
    {
        return new ScenarioDefinition
        {
            Name = scenario.Name,
            Description = scenario.Description,
            Background = background,
            Steps = scenario.Steps.Select(ParseStep).ToList(),
            Tags = scenario.Tags.Select(t => t.Name).ToList()
        };
    }
    
    // ScenarioOutline support removed - not available in newer Gherkin versions
    // Would need to implement custom parsing or use an older version if needed
    
    private StepDefinition ParseStep(Step step)
    {
        var (process, processedText) = _contextExtractor.ExtractContext(step.Text);
        
        return new StepDefinition
        {
            Type = ParseStepType(step.Keyword.Trim()),
            Text = step.Text,
            Process = process,
            ProcessedText = processedText
        };
    }
    
    
    private StepType ParseStepType(string keyword)
    {
        return keyword.ToLowerInvariant() switch
        {
            "given" => StepType.Given,
            "when" => StepType.When,
            "then" => StepType.Then,
            "and" => StepType.And,
            "but" => StepType.But,
            _ => StepType.And
        };
    }
}