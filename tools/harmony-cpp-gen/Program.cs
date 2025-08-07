using System.CommandLine;
using Gherkin;

namespace HarmonyCppGen;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("harmony-cpp-gen - Generate C++ Google Test files from Gherkin features");
        
        // Input option
        var inputOption = new Option<DirectoryInfo>(
            new[] { "--input", "-i" },
            getDefaultValue: () => new DirectoryInfo("Features"),
            "Input directory containing feature files");
        
        // Output option
        var outputOption = new Option<DirectoryInfo>(
            new[] { "--output", "-o" },
            getDefaultValue: () => new DirectoryInfo("generated"),
            "Output directory for generated C++ test files");
        
        // Feature filter option
        var filterOption = new Option<string>(
            new[] { "--filter", "-f" },
            "Filter feature files by name (e.g., 'BasicCommunication')");
        
        // Namespace option
        var namespaceOption = new Option<string>(
            new[] { "--namespace", "-n" },
            getDefaultValue: () => "zerobuffer::steps",
            "C++ namespace for generated tests");
        
        // Verbose option
        var verboseOption = new Option<bool>(
            new[] { "--verbose", "-v" },
            "Enable verbose output");
        
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(filterOption);
        rootCommand.AddOption(namespaceOption);
        rootCommand.AddOption(verboseOption);
        
        rootCommand.SetHandler(async (input, output, filter, ns, verbose) =>
        {
            var generator = new TestGenerator(verbose);
            await generator.GenerateTests(input!, output!, filter, ns!);
        }, inputOption, outputOption, filterOption, namespaceOption, verboseOption);
        
        // Add subcommands
        var listCommand = new Command("list", "List available feature files");
        listCommand.AddOption(inputOption);
        listCommand.SetHandler((input) =>
        {
            ListFeatures(input!);
        }, inputOption);
        
        var cleanCommand = new Command("clean", "Clean generated files");
        cleanCommand.AddOption(outputOption);
        cleanCommand.SetHandler((output) =>
        {
            CleanOutput(output!);
        }, outputOption);
        
        rootCommand.AddCommand(listCommand);
        rootCommand.AddCommand(cleanCommand);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static void ListFeatures(DirectoryInfo inputDir)
    {
        if (!inputDir.Exists)
        {
            Console.WriteLine($"Error: Input directory does not exist: {inputDir.FullName}");
            return;
        }
        
        var featureFiles = inputDir.GetFiles("*.feature", SearchOption.AllDirectories)
            .OrderBy(f => f.Name)
            .ToArray();
        
        Console.WriteLine($"Found {featureFiles.Length} feature files in {inputDir.FullName}:");
        foreach (var file in featureFiles)
        {
            var relativePath = Path.GetRelativePath(inputDir.FullName, file.FullName);
            Console.WriteLine($"  - {relativePath}");
        }
    }
    
    static void CleanOutput(DirectoryInfo outputDir)
    {
        if (!outputDir.Exists)
        {
            Console.WriteLine($"Output directory does not exist: {outputDir.FullName}");
            return;
        }
        
        var files = outputDir.GetFiles("*.cpp").Concat(outputDir.GetFiles("CMakeLists.txt"));
        var count = 0;
        
        foreach (var file in files)
        {
            Console.WriteLine($"Removing: {file.Name}");
            file.Delete();
            count++;
        }
        
        Console.WriteLine($"Cleaned {count} files from {outputDir.FullName}");
    }
}