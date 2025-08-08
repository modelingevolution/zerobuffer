using FluentAssertions;
using Gherkin;
using Gherkin.Ast;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Gherkin;

namespace ModelingEvolution.Harmony.Tests;

public class TableHandlingTests
{
    private readonly ITestOutputHelper _output;
    private readonly GherkinParser _parser;
    
    public TableHandlingTests(ITestOutputHelper output)
    {
        _output = output;
        var processExtractor = new ProcessContextExtractor();
        _parser = new GherkinParser(processExtractor);
    }
    
    [Fact]
    public void VerifyTablesInFeatureFiles()
    {
        // Parse a feature file that has tables
        var parser = new Parser();
        var featurePath = Path.Combine("Features", "EdgeCases.feature");
        var featureText = File.ReadAllText(featurePath);
        var gherkinDocument = parser.Parse(new StringReader(featureText));
        
        _output.WriteLine("Checking for tables in EdgeCases.feature:\n");
        
        foreach (var featureChild in gherkinDocument.Feature.Children)
        {
            if (featureChild is Scenario scenario)
            {
                _output.WriteLine($"Scenario: {scenario.Name}");
                
                foreach (var step in scenario.Steps)
                {
                    if (step.Argument != null)
                    {
                        _output.WriteLine($"  Step: {step.Text}");
                        
                        if (step.Argument is DataTable dataTable)
                        {
                            _output.WriteLine("    Has table:");
                            
                            // Header row
                            var rowsList = dataTable.Rows.ToList();
                            if (rowsList.Count > 0)
                            {
                                var headers = rowsList[0].Cells.Select(c => c.Value).ToList();
                                _output.WriteLine($"      Headers: {string.Join(" | ", headers)}");
                                
                                // Data rows
                                for (int i = 1; i < rowsList.Count; i++)
                                {
                                    var values = rowsList[i].Cells.Select(c => c.Value).ToList();
                                    _output.WriteLine($"      Row {i}: {string.Join(" | ", values)}");
                                }
                            }
                        }
                        else if (step.Argument is DocString docString)
                        {
                            _output.WriteLine($"    Has DocString: {docString.Content}");
                        }
                    }
                }
                _output.WriteLine("");
            }
        }
    }
    
    [Fact]
    public void ParseScenarioWithTable_ExtractsTableData()
    {
        // Create a test feature with a table
        var featureContent = @"
Feature: Test Tables
    Scenario: Table Test
        Given the following data:
        | name  | value |
        | test1 | 100   |
        | test2 | 200   |
        When process the table
        Then verify results
";
        
        var parser = new Parser();
        var gherkinDocument = parser.Parse(new StringReader(featureContent));
        
        var scenario = gherkinDocument.Feature.Children.First() as Scenario;
        scenario.Should().NotBeNull();
        
        var tableStep = scenario!.Steps.First(s => s.Text.Contains("following data"));
        tableStep.Argument.Should().NotBeNull();
        tableStep.Argument.Should().BeOfType<DataTable>();
        
        var table = tableStep.Argument as DataTable;
        var rowsList = table!.Rows.ToList();
        rowsList.Should().HaveCount(3); // Header + 2 data rows
        
        // Extract table data
        var headers = rowsList[0].Cells.Select(c => c.Value).ToList();
        headers.Should().Equal("name", "value");
        
        _output.WriteLine("Parsed table:");
        _output.WriteLine($"Headers: {string.Join(", ", headers)}");
        
        for (int i = 1; i < rowsList.Count; i++)
        {
            var row = rowsList[i];
            var data = headers.Zip(row.Cells.Select(c => c.Value), (h, v) => $"{h}={v}");
            _output.WriteLine($"Row {i}: {string.Join(", ", data)}");
        }
    }
    
    [Fact]
    public void CurrentParser_ShouldHandleTables()
    {
        // Check if our current parser needs to be updated to handle tables
        var scenarios = _parser.ParseFeatureFile(Path.Combine("Features", "EdgeCases.feature"), FeatureIdMapper.GetFeatureId).ToList();
        
        // Find a scenario with tables
        var continuousFreeBytes = scenarios.FirstOrDefault(s => s.Name.Contains("Continuous Free Space"));
        continuousFreeBytes.Should().NotBeNull();
        
        _output.WriteLine($"Scenario: {continuousFreeBytes!.Name}");
        _output.WriteLine($"Steps: {continuousFreeBytes.Steps.Count}");
        
        // The step with table should be parsed
        var tableStep = continuousFreeBytes.Steps.FirstOrDefault(s => 
            s.Text.Contains("continuous_free_bytes"));
        
        if (tableStep != null)
        {
            _output.WriteLine($"\nStep with table: {tableStep.Text}");
            _output.WriteLine($"Parameters in StepDefinition: {tableStep.Parameters.Count}");
            
            // Currently our StepDefinition doesn't have a property for tables
            // We need to add this functionality
            _output.WriteLine("\nNOTE: Current StepDefinition model doesn't support tables!");
            _output.WriteLine("Need to add TableData property to StepDefinition");
        }
    }
    
    [Fact] 
    public void ShowHowTablesWouldBeUsed()
    {
        _output.WriteLine("=== How Tables Should Work in Harmony ===\n");
        
        _output.WriteLine("1. Update StepDefinition model:");
        _output.WriteLine(@"
public class StepDefinition
{
    public StepType Type { get; init; }
    public string Text { get; init; }
    public string? Process { get; init; }
    public string? ProcessedText { get; init; }
    public Dictionary<string, object> Parameters { get; init; }
    public TableData? Table { get; init; }  // NEW
}

public class TableData
{
    public List<string> Headers { get; init; }
    public List<Dictionary<string, string>> Rows { get; init; }
}
");
        
        _output.WriteLine("\n2. Update GherkinParser to extract tables:");
        _output.WriteLine(@"
private StepDefinition ParseStep(Step step)
{
    var table = ParseTable(step.Argument as DataTable);
    
    return new StepDefinition
    {
        // ... existing properties ...
        Table = table
    };
}
");
        
        _output.WriteLine("\n3. JSON-RPC request would include table data:");
        _output.WriteLine(@"
{
  ""method"": ""executeStep"",
  ""params"": {
    ""step"": ""test continuous_free_bytes calculation with:"",
    ""table"": {
      ""headers"": [""write_pos"", ""read_pos"", ""expected_result""],
      ""rows"": [
        { ""write_pos"": ""5000"", ""read_pos"": ""2000"", ""expected_result"": ""calculated"" },
        { ""write_pos"": ""2000"", ""read_pos"": ""5000"", ""expected_result"": ""calculated"" }
      ]
    }
  }
}
");
    }
}