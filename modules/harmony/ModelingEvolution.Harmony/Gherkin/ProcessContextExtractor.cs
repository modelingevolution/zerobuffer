using System.Text.RegularExpressions;

namespace ModelingEvolution.Harmony.Gherkin;

/// <summary>
/// Extracts process context from Gherkin step text
/// </summary>
public interface IProcessContextExtractor
{
    (string? Process, string ProcessedText) ExtractContext(string stepText);
}

public class ProcessContextExtractor : IProcessContextExtractor
{
    private static readonly List<Regex> ProcessPatterns = new()
    {
        // Pattern: the 'process' process
        new Regex(@"^the\s+'(?<process>\w+)'\s+process\s+(?<rest>.*)$", RegexOptions.IgnoreCase),
        
        // Pattern: 'process' process
        new Regex(@"^'(?<process>\w+)'\s+process\s+(?<rest>.*)$", RegexOptions.IgnoreCase),
        
        // Pattern: the 'process' 
        new Regex(@"^the\s+'(?<process>\w+)'\s+(?<rest>.*)$", RegexOptions.IgnoreCase),
        
        // Pattern: 'process' at start
        new Regex(@"^'(?<process>\w+)'\s+(?<rest>.*)$", RegexOptions.IgnoreCase),
        
        // Pattern: the writer/reader is 'platform'
        new Regex(@"^the\s+(?<process>writer|reader)\s+is\s+'(?<platform>\w+)'(?<rest>.*)$", RegexOptions.IgnoreCase),
        
        // Pattern: two readers/writers 'platform1' and 'platform2'
        new Regex(@"^two\s+(?<process>readers|writers)\s+'(?<platform1>\w+)'\s+and\s+'(?<platform2>\w+)'(?<rest>.*)$", RegexOptions.IgnoreCase)
    };
    
    public (string? Process, string ProcessedText) ExtractContext(string stepText)
    {
        foreach (var pattern in ProcessPatterns)
        {
            var match = pattern.Match(stepText);
            if (match.Success)
            {
                var process = match.Groups["process"].Value;
                
                // For the "two readers/writers" pattern, we need special handling
                if (process == "readers" || process == "writers")
                {
                    // Convert plural to singular
                    process = process.TrimEnd('s');
                }
                
                // For patterns that have rest group, use it. Otherwise keep original text
                var rest = match.Groups["rest"].Success ? match.Groups["rest"].Value : stepText;
                
                return (process, rest.Trim());
            }
        }
        
        // No process context found
        return (null, stepText);
    }
}