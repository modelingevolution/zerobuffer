using System.Text;
using System.Text.RegularExpressions;
using Gherkin.Ast;

namespace CppTestGenerator;

public class TestFileGenerator
{
    private readonly Feature _feature;
    private readonly string _fileName;
    private readonly StringBuilder _output = new();
    private int _indentLevel = 0;
    
    public TestFileGenerator(Feature feature, string fileName)
    {
        _feature = feature;
        _fileName = fileName;
    }
    
    public string Generate()
    {
        WriteHeader();
        WriteIncludes();
        WriteTestFixture();
        WriteTests();
        
        return _output.ToString();
    }
    
    private void WriteHeader()
    {
        WriteLine("// Generated from: " + _fileName);
        WriteLine("// DO NOT EDIT - This file is auto-generated");
        WriteLine();
    }
    
    private void WriteIncludes()
    {
        WriteLine("#include <gtest/gtest.h>");
        WriteLine("#include <memory>");
        WriteLine("#include <string>");
        WriteLine("#include <vector>");
        WriteLine("#include \"step_definitions/step_registry.h\"");
        WriteLine("#include \"step_definitions/test_context.h\"");
        WriteLine("#include \"step_definitions/basic_communication_steps.h\"");
        WriteLine();
        WriteLine("using namespace zerobuffer::steps;");
        WriteLine();
    }
    
    private void WriteTestFixture()
    {
        WriteLine("class BasicCommunicationTest : public ::testing::Test {");
        WriteLine("protected:");
        Indent();
        WriteLine("StepRegistry& registry = StepRegistry::getInstance();");
        WriteLine("TestContext context;");
        WriteLine();
        WriteLine("void SetUp() override {");
        Indent();
        WriteLine("// Register step definitions");
        WriteLine("registerBasicCommunicationSteps();");
        WriteLine("context.reset();");
        Unindent();
        WriteLine("}");
        WriteLine();
        WriteLine("void TearDown() override {");
        Indent();
        WriteLine("// Clean up any resources");
        WriteLine("context.reset();");
        Unindent();
        WriteLine("}");
        WriteLine();
        WriteLine("bool ExecuteStep(const std::string& step) {");
        Indent();
        WriteLine("return registry.executeStep(step, context);");
        Unindent();
        WriteLine("}");
        Unindent();
        WriteLine("};");
        WriteLine();
    }
    
    private void WriteTests()
    {
        foreach (var child in _feature.Children)
        {
            if (child is Scenario scenario)
            {
                WriteScenarioTest(scenario);
            }
        }
    }
    
    private void WriteScenarioTest(Scenario scenario)
    {
        // Generate test name from scenario name
        var testName = SanitizeTestName(scenario.Name);
        
        WriteLine($"TEST_F(BasicCommunicationTest, {testName}) {{");
        Indent();
        
        // Add a comment with the original scenario name
        WriteLine($"// Scenario: {scenario.Name}");
        WriteLine();
        
        // Process background steps if any
        if (_feature.Children.Any(c => c is Background))
        {
            var background = _feature.Children.First(c => c is Background) as Background;
            if (background != null)
            {
                WriteLine("// Background steps");
                foreach (var step in background.Steps)
                {
                    WriteStepExecution(step);
                }
                WriteLine();
            }
        }
        
        // Process scenario steps
        WriteLine("// Scenario steps");
        foreach (var step in scenario.Steps)
        {
            WriteStepExecution(step);
        }
        
        Unindent();
        WriteLine("}");
        WriteLine();
    }
    
    private void WriteStepExecution(Step step)
    {
        var stepText = EscapeString(step.Text);
        var keyword = step.Keyword.Trim();
        
        // Add comment with original step
        WriteLine($"// {keyword} {step.Text}");
        
        // Generate ASSERT or EXPECT based on step type
        if (keyword == "Then")
        {
            WriteLine($"ASSERT_TRUE(ExecuteStep(\"{stepText}\")) << \"Failed: {stepText}\";");
        }
        else
        {
            WriteLine($"ASSERT_TRUE(ExecuteStep(\"{stepText}\")) << \"Failed: {stepText}\";");
        }
    }
    
    private string SanitizeTestName(string name)
    {
        // Remove special characters and convert to valid C++ identifier
        var sanitized = Regex.Replace(name, @"[^\w\d]", "_");
        
        // Remove consecutive underscores
        sanitized = Regex.Replace(sanitized, @"_+", "_");
        
        // Remove leading/trailing underscores
        sanitized = sanitized.Trim('_');
        
        // Ensure it doesn't start with a number
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
        {
            sanitized = "Test_" + sanitized;
        }
        
        return sanitized;
    }
    
    private string EscapeString(string text)
    {
        // Escape backslashes first, then quotes
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
    
    private void WriteLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _output.AppendLine();
        }
        else
        {
            _output.AppendLine(new string(' ', _indentLevel * 4) + line);
        }
    }
    
    private void Indent() => _indentLevel++;
    private void Unindent() => _indentLevel = Math.Max(0, _indentLevel - 1);
}