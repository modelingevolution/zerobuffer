using System.CommandLine;
using System.Text;
using Gherkin;
using Gherkin.Ast;

namespace CppTestGenerator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Generate C++ Google Test files from Gherkin feature files");
        
        var inputOption = new Option<DirectoryInfo>(
            new[] { "--input", "-i" },
            "Input directory containing feature files") { IsRequired = true };
        
        var outputOption = new Option<DirectoryInfo>(
            new[] { "--output", "-o" },
            "Output directory for generated C++ test files") { IsRequired = true };
        
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        
        rootCommand.SetHandler(async (inputDir, outputDir) =>
        {
            await GenerateTests(inputDir!, outputDir!);
        }, inputOption, outputOption);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    static async Task GenerateTests(DirectoryInfo inputDir, DirectoryInfo outputDir)
    {
        if (!outputDir.Exists)
            outputDir.Create();
        
        var parser = new Parser();
        var featureFiles = inputDir.GetFiles("*.feature", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Name)
            .ToArray();
        
        Console.WriteLine($"Found {featureFiles.Length} feature files in {inputDir.FullName}");
        
        // Only process BasicCommunication for now
        var basicCommFile = featureFiles.FirstOrDefault(f => f.Name.Contains("BasicCommunication"));
        if (basicCommFile != null)
        {
            try
            {
                Console.WriteLine($"Processing: {basicCommFile.Name}");
                var gherkinDocument = parser.Parse(basicCommFile.FullName);
                
                if (gherkinDocument.Feature == null)
                {
                    Console.WriteLine($"  Skipping {basicCommFile.Name} - no feature found");
                    return;
                }
                
                var generator = new TestFileGenerator(gherkinDocument.Feature, basicCommFile.Name);
                var cppContent = generator.Generate();
                
                // Generate output filename
                var outputFileName = "test_basic_communication_generated.cpp";
                var outputPath = Path.Combine(outputDir.FullName, outputFileName);
                
                await File.WriteAllTextAsync(outputPath, cppContent);
                Console.WriteLine($"  Generated: {outputFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error processing {basicCommFile.Name}: {ex.Message}");
                Console.WriteLine($"  Stack trace: {ex.StackTrace}");
            }
        }
        else
        {
            Console.WriteLine("No BasicCommunication feature file found!");
        }
    }
}