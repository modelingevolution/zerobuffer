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
        
        var featureFileName = Path.GetFileNameWithoutExtension(path);
        BackgroundDefinition? background = null;
        
        foreach (var child in feature.Children)
        {
            if (child is Background bg)
            {
                background = ParseBackground(bg);
            }
            else if (child is Scenario scenario)
            {
                var scenarioDefinition = ParseScenario(scenario, background);
                scenarioDefinition.FeatureFile = featureFileName;
                yield return scenarioDefinition;
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
        var stepParser = new StepParser(_contextExtractor);
        
        return new BackgroundDefinition
        {
            Steps = background.Steps.Select(step => stepParser.ParseStep(step)).ToList()
        };
    }
    
    private ScenarioDefinition ParseScenario(Scenario scenario, BackgroundDefinition? background)
    {
        var stepParser = new StepParser(_contextExtractor);
        
        return new ScenarioDefinition
        {
            Name = scenario.Name,
            Description = scenario.Description,
            Background = background,
            Steps = scenario.Steps.Select(step => stepParser.ParseStep(step)).ToList(),
            Tags = scenario.Tags.Select(t => t.Name).ToList()
        };
    }
    
    // ScenarioOutline support removed - not available in newer Gherkin versions
    // Would need to implement custom parsing or use an older version if needed
    
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
    
    private class StepParser
    {
        private readonly IProcessContextExtractor _contextExtractor;
        private string? _lastProcess;
        private StepType _lastStepType = StepType.Given;
        
        public StepParser(IProcessContextExtractor contextExtractor)
        {
            _contextExtractor = contextExtractor;
        }
        
        public StepDefinition ParseStep(Step step)
        {
            var stepType = ParseStepType(step.Keyword.Trim());
            var (process, processedText) = _contextExtractor.ExtractContext(step.Text);
            
            // Handle And/But inheritance
            if (stepType == StepType.And || stepType == StepType.But)
            {
                // If no process was extracted and we have a previous process, use it
                if (process == null && _lastProcess != null)
                {
                    process = _lastProcess;
                }
            }
            else
            {
                // For Given/When/Then, update the last step type
                _lastStepType = stepType;
            }
            
            // Always update last process if we have one
            if (process != null)
            {
                _lastProcess = process;
            }
            
            return new StepDefinition
            {
                Type = stepType,
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
}