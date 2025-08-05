using ModelingEvolution.Harmony.Core;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Base class for tests that provides common test configuration
/// </summary>
public abstract class TestBase
{
    /// <summary>
    /// Creates a test configuration suitable for unit tests
    /// </summary>
    protected static MultiprocessConfiguration CreateTestConfiguration()
    {
        return new MultiprocessConfiguration
        {
            FeaturesPath = "Features",
            DefaultTimeoutMs = 5000,
            ProcessInitializationDelayMs = 100,
            Platforms = new Dictionary<string, PlatformConfiguration>
            {
                ["csharp"] = new PlatformConfiguration
                {
                    Executable = "mock-csharp",
                    Arguments = "test"
                },
                ["python"] = new PlatformConfiguration
                {
                    Executable = "mock-python",
                    Arguments = "test"
                },
                ["cpp"] = new PlatformConfiguration
                {
                    Executable = "mock-cpp",
                    Arguments = "test"
                }
            }
        };
    }
}