using System.Text;
using System.Text.Json;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.Execution;

namespace ZeroBuffer.Harmony.Tests.CodeGeneration;

public class TestGenerator
{
    private readonly IGherkinParser _parser;
    private readonly IScenarioGenerator _scenarioGenerator;
    private readonly string[] _platforms;
    private readonly List<(string fieldName, string json)> _jsonFields = new();
    
    public TestGenerator(IGherkinParser parser, IScenarioGenerator scenarioGenerator, string[] platforms)
    {
        _parser = parser;
        _scenarioGenerator = scenarioGenerator;
        _platforms = platforms;
    }
    
    public async Task GenerateTestsAsync(string featuresPath, string outputPath)
    {
        // Use the scenario generator to generate all scenarios
        var scenarioExecutions = _scenarioGenerator.GenerateScenarios(featuresPath, _platforms);
        
        // Track all generated test classes for the registry
        var allTestClasses = new List<(string namespaceName, string className)>();
        
        // Group scenarios by platform combination and then by feature file
        var scenariosByPlatformAndFeature = scenarioExecutions
            .GroupBy(s => GetPlatformCombination(s))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(s => GetFeatureFileName(s.Scenario)).ToList()
            );
        
        foreach (var (platformCombination, featureGroups) in scenariosByPlatformAndFeature)
        {
            // Create directory for platform combination (e.g., csharp_python)
            var platformDir = Path.Combine(outputPath, platformCombination);
            Directory.CreateDirectory(platformDir);
            
            foreach (var featureGroup in featureGroups)
            {
                var featureFileName = featureGroup.Key;
                var featureClassName = $"{ConvertToClassName(featureFileName)}Tests";
                var outputFile = Path.Combine(platformDir, $"{featureClassName}.generated.cs");
                
                // Clear fields for this class
                _jsonFields.Clear();
                
                var testMethods = new StringBuilder();
                var scenarioList = new List<ScenarioExecution>();
                var testCount = 0;
                
                // Generate test methods for each scenario in this feature
                foreach (var execution in featureGroup)
                {
                    var testMethod = GenerateTestMethod(execution);
                    testMethods.AppendLine(testMethod);
                    testMethods.AppendLine();
                    scenarioList.Add(execution);
                    testCount++;
                }
                
                // Generate the complete test class with namespace based on platform combination
                var namespaceName = $"ZeroBuffer.Harmony.Tests.{ConvertToNamespacePart(platformCombination)}";
                var testClass = GenerateTestClass(featureClassName, testMethods.ToString(), namespaceName, _jsonFields, scenarioList);
                
                // Track this test class
                allTestClasses.Add((namespaceName, featureClassName));
                
                // Write to file
                await File.WriteAllTextAsync(outputFile, testClass);
                Console.WriteLine($"Generated {outputFile} with {testCount} tests");
            }
        }
        
        // Generate the FeatureRegistry
        var registryPath = Path.Combine(outputPath, "FeatureRegistry.generated.cs");
        var registryClass = GenerateFeatureRegistry(allTestClasses);
        await File.WriteAllTextAsync(registryPath, registryClass);
        Console.WriteLine($"Generated {registryPath} with registry for {allTestClasses.Count} test classes");
    }
    
    private string GetPlatformCombination(ScenarioExecution scenario)
    {
        // Create platform combination folder name (e.g., "csharp_python")
        var platforms = scenario.Platforms.GetAllProcesses()
            .OrderBy(p => p)
            .Select(p => scenario.Platforms.GetPlatform(p))
            .ToList();
        
        return string.Join("_", platforms);
    }
    
    private string ConvertToNamespacePart(string platformCombination)
    {
        // Convert platform combination to namespace part (e.g., "csharp_python" -> "Csharp_Python")
        var parts = platformCombination.Split('_');
        var capitalizedParts = parts.Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower());
        return string.Join("_", capitalizedParts);
    }
    
    private string GetFeatureFileName(ScenarioDefinition scenario)
    {
        // Use the FeatureFile property set by the parser
        return scenario.FeatureFile ?? "Unknown";
    }
    
    private string ConvertToClassName(string featureFileName)
    {
        // Just return the feature file name as-is for the class name
        if (featureFileName.Contains("-"))
        {
            string[] parts = featureFileName.Split('-');
            return parts[1];
        }
        return featureFileName;
    }
    
    
    private string GenerateTestMethod(ScenarioExecution scenario)
    {
        var testName = GenerateTestName(scenario);
        var displayName = GenerateDisplayName(scenario);
        var jsonFieldName = $"{testName}_Json";
        var scenarioJson = SerializeScenarioForCode(scenario);
        var traits = GenerateTraits(scenario);
        
        // Store the JSON field reference for later
        _jsonFields.Add((jsonFieldName, scenarioJson));
        
        return $@"    [Fact(DisplayName = ""{displayName}"")]
{traits}    public async Task {testName}()
    {{
        var scenario = DeserializeScenario({jsonFieldName});
        await ExecuteScenarioAsync(scenario);
    }}";
    }
    
    private string GenerateTestName(ScenarioExecution scenario)
    {
        // Generate a unique test name based on scenario name and platform combination
        var scenarioName = SanitizeForMethodName(scenario.Scenario.Name);
        var platforms = scenario.Platforms.GetAllProcesses()
            .Select(p => $"{SanitizeForMethodName(p)}_{SanitizeForMethodName(scenario.Platforms.GetPlatform(p))}")
            .OrderBy(p => p);
        
        return $"{scenarioName}_{string.Join("_", platforms)}";
    }
    
    private string GenerateDisplayName(ScenarioExecution scenario)
    {
        var platforms = scenario.Platforms.GetAllProcesses()
            .Select(p => $"{p}={scenario.Platforms.GetPlatform(p)}")
            .OrderBy(p => p);
        
        return $"{scenario.Scenario.Name} [{string.Join(", ", platforms)}]";
    }
    
    private string GenerateTraits(ScenarioExecution scenario)
    {
        var traits = new List<string>();
        
        // Add scenario name trait
        traits.Add($@"    [Trait(""Scenario"", ""{scenario.Scenario.Name}"")]");
        
        // Add platform combination trait (e.g., "cpp/cpp" or "csharp/python")
        var platformCombination = string.Join("/", 
            scenario.Platforms.GetAllProcesses()
                .OrderBy(p => p)
                .Select(p => scenario.Platforms.GetPlatform(p)));
        traits.Add($@"    [Trait(""Platform"", ""{platformCombination}"")]");
        
        // Add individual platform traits for filtering
        var platforms = scenario.Platforms.GetAllProcesses()
            .Select(p => scenario.Platforms.GetPlatform(p))
            .Distinct()
            .OrderBy(p => p);
        
        foreach (var platform in platforms)
        {
            traits.Add($@"    [Trait(""Uses"", ""{platform}"")]");
        }
        
        // Add feature trait
        var featureName = GetFeatureFileName(scenario.Scenario);
        traits.Add($@"    [Trait(""Feature"", ""{featureName}"")]");
        
        // Add tags as traits
        foreach (var tag in scenario.Scenario.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !tag.StartsWith("feature:"))
            {
                traits.Add($@"    [Trait(""Tag"", ""{tag}"")]");
            }
        }
        
        return string.Join("\n", traits);
    }
    
    private string SanitizeForMethodName(string input)
    {
        // Replace non-alphanumeric characters with underscores
        var result = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
            }
            else
            {
                result.Append('_');
            }
        }
        
        // Remove consecutive underscores
        var sanitized = result.ToString();
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }
        
        return sanitized.Trim('_');
    }
    
    private string SerializeScenarioForCode(ScenarioExecution scenario)
    {
        // Create a serializable version of ScenarioExecution
        var data = new
        {
            TestId = scenario.TestId,
            FeatureId = scenario.Scenario.FeatureId,
            ScenarioName = scenario.Scenario.Name,
            ScenarioDescription = scenario.Scenario.Description,
            Tags = scenario.Scenario.Tags,
            Platforms = scenario.Platforms.GetAllProcesses()
                .ToDictionary(p => p, p => scenario.Platforms.GetPlatform(p)),
            Background = scenario.Scenario.Background?.Steps.Select(SerializeStep).ToList(),
            Steps = scenario.Scenario.Steps.Select(SerializeStep).ToList()
        };
        
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
        { 
            WriteIndented = false // Compact for embedding in code
        });
        
        // Escape quotes for C# string
        return json.Replace("\"", "\"\"");
    }
    
    private object SerializeStep(StepDefinition step)
    {
        return new
        {
            Type = step.Type.ToString(),
            Text = step.Text,
            Process = step.Process,
            ProcessedText = step.ProcessedText,
            Parameters = step.Parameters
        };
    }
    
    private string GenerateTestClass(string className, string testMethods, string namespaceName, 
        List<(string fieldName, string json)> jsonFields, List<ScenarioExecution> scenarios)
    {
        // Generate static JSON fields
        var jsonFieldsCode = new StringBuilder();
        foreach (var (fieldName, json) in jsonFields)
        {
            jsonFieldsCode.AppendLine($@"    private static readonly string {fieldName} = @""{json}"";");
        }
        
        // Generate static Scenarios property
        var scenariosCode = GenerateScenariosProperty(jsonFields.Select(f => f.fieldName).ToList());
        
        return $@"// <auto-generated />
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;
using ModelingEvolution.Harmony.ProcessManagement;
using Microsoft.Extensions.Logging.Abstractions;
using ZeroBuffer.Harmony.Tests;

namespace {namespaceName};

public class {className} : HarmonyTestBase
{{
    public {className}(ITestOutputHelper output) : base(output) {{ }}

    #region Static Test Data
{jsonFieldsCode}
    #endregion

    #region Scenarios Collection
{scenariosCode}
    #endregion

    #region Test Methods
{testMethods}
    #endregion
}}";
    }
    
    private string GenerateScenariosProperty(List<string> jsonFieldNames)
    {
        var scenarioInitializers = new StringBuilder();
        for (int i = 0; i < jsonFieldNames.Count; i++)
        {
            var fieldName = jsonFieldNames[i];
            if (i < jsonFieldNames.Count - 1)
            {
                scenarioInitializers.AppendLine($"        DeserializeScenarioStatic({fieldName}),");
            }
            else
            {
                scenarioInitializers.AppendLine($"        DeserializeScenarioStatic({fieldName})");
            }
        }
        
        return $@"    public static IEnumerable<ScenarioExecution> Scenarios => new[]
    {{
{scenarioInitializers}    }};";
    }
    
    private string GenerateFeatureRegistry(List<(string namespaceName, string className)> testClasses)
    {
        var typeRegistrations = new StringBuilder();
        var orderedClasses = testClasses.OrderBy(x => x.namespaceName).ThenBy(x => x.className).ToList();
        
        for (int i = 0; i < orderedClasses.Count; i++)
        {
            var (namespaceName, className) = orderedClasses[i];
            if (i < orderedClasses.Count - 1)
            {
                typeRegistrations.AppendLine($"        typeof({namespaceName}.{className}),");
            }
            else
            {
                typeRegistrations.AppendLine($"        typeof({namespaceName}.{className})");
            }
        }
        
        return $@"// <auto-generated />
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;

namespace ZeroBuffer.Harmony.Tests;

/// <summary>
/// Registry of all generated feature test classes
/// </summary>
public static class FeatureRegistry
{{
    /// <summary>
    /// All feature test class types
    /// </summary>
    public static readonly Type[] AllFeatureTypes = new[]
    {{
{typeRegistrations}
    }};
    
    /// <summary>
    /// Get all scenarios from all feature classes
    /// </summary>
    public static IEnumerable<ScenarioExecution> GetAllScenarios()
    {{
        foreach (var featureType in AllFeatureTypes)
        {{
            var scenariosProperty = featureType.GetProperty(""Scenarios"", 
                BindingFlags.Public | BindingFlags.Static);
            
            if (scenariosProperty != null)
            {{
                var scenarios = scenariosProperty.GetValue(null) as IEnumerable<ScenarioExecution>;
                if (scenarios != null)
                {{
                    foreach (var scenario in scenarios)
                    {{
                        yield return scenario;
                    }}
                }}
            }}
        }}
    }}
    
    /// <summary>
    /// Get scenarios filtered by platform
    /// </summary>
    public static IEnumerable<ScenarioExecution> GetScenariosForPlatform(string platform)
    {{
        return GetAllScenarios()
            .Where(s => s.Platforms.GetAllProcesses()
                .Any(p => s.Platforms.GetPlatform(p) == platform));
    }}
    
    /// <summary>
    /// Get scenarios for a specific feature
    /// </summary>
    public static IEnumerable<ScenarioExecution> GetScenariosForFeature(string featureName)
    {{
        return GetAllScenarios()
            .Where(s => s.Scenario.FeatureFile == featureName);
    }}
}}";
    }
}