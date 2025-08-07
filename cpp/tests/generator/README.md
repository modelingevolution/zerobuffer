# C++ Test Generator

This is a C# console application that generates C++ Google Test files from Gherkin feature files.

## Location
`cpp/tests/generator/`

## How to Build
```bash
cd cpp/tests/generator
dotnet build
```

## How to Run
```bash
# From cpp/tests/generator directory:
dotnet run -- --input ../../../ZeroBuffer.Harmony.Tests/Features --output generated

# Or run the built executable directly:
./bin/Debug/net9.0/generator --input ../../../ZeroBuffer.Harmony.Tests/Features --output generated
```

## What it Does
1. Reads Gherkin feature files from the input directory
2. Parses them using the Gherkin NuGet package (same as Harmony uses)
3. Generates C++ Google Test code that:
   - Creates a test fixture with StepRegistry
   - Converts each scenario to a TEST_F
   - Calls ExecuteStep() for each Gherkin step
   - Includes proper ASSERT_TRUE with error messages

## Output
Generated files go to `cpp/tests/generator/generated/`:
- `test_basic_communication_generated.cpp` - Test file for BasicCommunication feature
- `CMakeLists.txt` - CMake configuration for building the tests

## Integration with Build System
The main CMakeLists.txt in `cpp/tests/` checks for generated tests and includes them if found.

## Running Generated Tests Locally
```bash
# From cpp directory:
./test.sh 1.1  # Runs Test_1_1_* from generated tests
```

## Files
- `Program.cs` - Main entry point, command line parsing
- `TestFileGenerator.cs` - Generates C++ code from Gherkin AST
- `generator.csproj` - Project file with dependencies