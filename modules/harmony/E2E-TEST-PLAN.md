# E2E Test Plan for ZeroBuffer with Harmony

## Overview

Create a complete end-to-end testing setup that uses Harmony to orchestrate cross-platform tests for ZeroBuffer. The key innovation is using xUnit's `[Theory]` attribute with custom data discovery to generate 400+ test cases from Gherkin feature files.

## Architecture

```
ZeroBuffer.Harmony.Tests (xUnit project)
├── Features/                    # All .feature files (copied to output)
├── HarmonyTestsDiscoverer.cs   # IEnumerable<object[]> for Theory data
├── ZeroBufferE2ETests.cs       # Single test class with [Theory]
└── ZeroBuffer.Harmony.Tests.csproj

References:
- ModelingEvolution.Harmony
- xUnit
```

## Implementation Plan

### 1. Create ZeroBuffer.Harmony.Tests Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../ModelingEvolution.Harmony/ModelingEvolution.Harmony.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Features/**/*.feature">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

### 2. Test Discovery Implementation

```csharp
public class HarmonyTestsDiscoverer : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        var config = new HarmonyConfiguration
        {
            FeatureFiles = "Features/**/*.feature",
            Platforms = new[] { "csharp", "python", "cpp" }
        };
        
        var generator = new ScenarioGenerator(/* dependencies */);
        var scenarios = generator.GenerateScenarios(config);
        
        foreach (var scenario in scenarios)
        {
            yield return new object[] { scenario };
        }
    }
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

### 3. The Main Test Class (2 lines as requested!)

```csharp
public class ZeroBufferE2ETests
{
    [Theory]
    [ClassData(typeof(HarmonyTestsDiscoverer))]
    public async Task RunScenario(ScenarioExecution scenario) 
        => await HarmonyTestRunner.ExecuteScenarioAsync(scenario);
}
```

### 4. Test Discovery Verification

```bash
# Count discovered tests without running them
dotnet test --list-tests | grep -c "RunScenario"
# Should show 441 tests (57 scenarios × 3³ platform combinations)
```

### 5. Running Single Happy Path

```bash
# Run specific test by display name
dotnet test --filter "DisplayName~'csharp/python | Test 1.1 - Simple Write-Read Cycle'"
```

## Key Context to Preserve

### Critical Files Created
1. `/ZeroBuffer.Harmony.Tests/ZeroBuffer.Harmony.Tests.csproj` - Project file
2. `/ZeroBuffer.Harmony.Tests/HarmonyTestsDiscoverer.cs` - Test data discovery
3. `/ZeroBuffer.Harmony.Tests/ZeroBufferE2ETests.cs` - Main test class
4. `/ZeroBuffer.Harmony.Tests/Features/**/*.feature` - All feature files

### Key Concepts
- **Theory-based Discovery**: xUnit's `[Theory]` with `[ClassData]` generates all test permutations
- **ScenarioExecution**: The data object passed to each test containing all scenario info
- **HarmonyTestRunner**: Static class that executes scenarios using ProcessManager and StepExecutor
- **Test Naming**: Format is `{platforms} | {scenario name}` for easy filtering

### Integration Points
- Feature files are parsed by Harmony's GherkinParser
- ScenarioGenerator creates all platform combinations
- ProcessManager starts serve processes (C#/Python/C++)
- StepExecutor sends JSON-RPC commands to processes
- Test results are aggregated and reported by xUnit

### Expected Outcomes
- 441 tests discovered (57 base scenarios × various platform combinations)
- Each test shows as individual entry in test explorer
- Can filter and run specific scenarios
- Full E2E execution through real processes

## Next Steps After Context Compaction

1. Implement HarmonyTestRunner.ExecuteScenarioAsync
2. Create serve processes for Python and C++
3. Implement actual ZeroBuffer step definitions
4. Add proper test result reporting
5. Set up CI/CD pipeline for cross-platform testing