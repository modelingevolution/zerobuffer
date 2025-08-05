using System.Text.Json;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;
using ModelingEvolution.Harmony.Execution;
using ZeroBuffer.Harmony.Tests.CodeGeneration;

if (args.Length < 2)
{
    Console.WriteLine("Usage: TestGenerator <featuresPath> <outputPath> [configPath]");
    Console.WriteLine("Example: TestGenerator Features Generated");
    Console.WriteLine("         TestGenerator Features Generated harmony-config.json");
    return 1;
}

var featuresPath = args[0];
var outputPath = args[1];
var configPath = args.Length > 2 ? args[2] : "harmony-config.json";

// Load configuration to get platforms
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"ERROR: Configuration file '{configPath}' not found!");
    Console.Error.WriteLine("The harmony-config.json file is required to determine which platforms to generate tests for.");
    return 1;
}

Console.WriteLine($"Loading configuration from {configPath}");
var json = await File.ReadAllTextAsync(configPath);
var config = JsonSerializer.Deserialize<MultiprocessConfiguration>(json, new JsonSerializerOptions 
{ 
    PropertyNameCaseInsensitive = true 
});

if (config?.Platforms == null || config.Platforms.Count == 0)
{
    Console.Error.WriteLine("ERROR: No platforms defined in configuration file!");
    return 1;
}

var platforms = config.Platforms.Keys.ToArray();

Console.WriteLine($"Generating tests from {featuresPath} to {outputPath}");
Console.WriteLine($"Platforms: {string.Join(", ", platforms)}");

// Ensure output directory exists
Directory.CreateDirectory(outputPath);

// Initialize dependencies
var processContextExtractor = new ProcessContextExtractor();
var parser = new GherkinParser(processContextExtractor);
var scenarioGenerator = new ScenarioGenerator(parser);

// Generate tests
var generator = new TestGenerator(parser, scenarioGenerator, platforms);
await generator.GenerateTestsAsync(featuresPath, outputPath);

Console.WriteLine("Test generation completed successfully");
return 0;