using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Execution;

namespace ZeroBuffer.Harmony.Tests;

public class StepCompletenessTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Process? _serveProcess;
    private JsonRpc? _jsonRpc;
    
    public StepCompletenessTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public async Task ValidateServeHasAllRequiredSteps()
    {
        // Start the C# serve process
        await StartServeProcessAsync();
        
        // Get all available step definitions from the serve
        _output.WriteLine("Discovering available step definitions from serve...");
        var discovery = await _jsonRpc!.InvokeAsync<DiscoverResponse>("discover");
        
        _output.WriteLine($"Serve has {discovery.Steps.Count} step definitions registered");
        
        // Load all generated CSharp-CSharp test scenarios using FeatureRegistry
        var testScenarios = FeatureRegistry.GetAllScenarios()
            .Where(s => s.Platforms.GetAllProcesses()
                .All(p => s.Platforms.GetPlatform(p) == "csharp"))
            .ToList();
        _output.WriteLine($"Found {testScenarios.Count} C# test scenarios");
        
        // Collect all unique steps used in scenarios
        var requiredSteps = new HashSet<(string type, string text)>();
        
        foreach (var scenario in testScenarios)
        {
            // Collect background steps if present
            if (scenario.Scenario.Background != null)
            {
                foreach (var step in scenario.Scenario.Background.Steps)
                {
                    var stepText = step.ProcessedText?.Trim() ?? step.Text?.Trim() ?? "";
                    var stepType = step.Type.ToString().ToLower();
                    if (!string.IsNullOrEmpty(stepText))
                    {
                        requiredSteps.Add((stepType, stepText));
                    }
                }
            }
            
            // Collect scenario steps
            foreach (var step in scenario.Scenario.Steps)
            {
                var stepText = step.ProcessedText?.Trim() ?? step.Text?.Trim() ?? "";
                var stepType = step.Type.ToString().ToLower();
                if (!string.IsNullOrEmpty(stepText))
                {
                    requiredSteps.Add((stepType, stepText));
                }
            }
        }
        
        _output.WriteLine($"Scenarios use {requiredSteps.Count} unique steps");
        
        // Check which required steps are missing from serve
        var missingSteps = new List<string>();
        
        foreach (var (stepType, stepText) in requiredSteps.OrderBy(s => s.type).ThenBy(s => s.text))
        {
            var isSupported = discovery.Steps.Any(def => 
                def.Type == stepType && 
                IsRegexMatch(stepText, def.Pattern));
            
            if (!isSupported)
            {
                missingSteps.Add($"{stepType}: {stepText}");
            }
        }
        
        _output.WriteLine($"\nValidation complete:");
        _output.WriteLine($"  Required unique steps: {requiredSteps.Count}");
        _output.WriteLine($"  Missing from serve: {missingSteps.Count}");
        
        if (missingSteps.Any())
        {
            _output.WriteLine("\nMissing step definitions in serve:");
            foreach (var step in missingSteps.Take(30)) // Show first 30
            {
                _output.WriteLine($"  - {step}");
            }
            
            if (missingSteps.Count > 30)
            {
                _output.WriteLine($"  ... and {missingSteps.Count - 30} more");
            }
        }
        
        // Assert that serve has all required steps
        Assert.True(missingSteps.Count == 0, 
            $"Serve is missing {missingSteps.Count} required step definitions. First few: {string.Join(", ", missingSteps.Take(5))}");
    }
    
    private bool IsRegexMatch(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }
        catch (Exception)
        {
            // If regex is malformed, assume no match
            return false;
        }
    }
    
    private async Task StartServeProcessAsync()
    {
        var servePath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), 
            "ZeroBuffer.Serve"));
        
        if (!File.Exists(servePath))
        {
            throw new Exception($"ZeroBuffer.Serve executable not found at: {servePath}. Please build the project first.");
        }
        
        _output.WriteLine($"Starting serve process: {servePath}");
        
        _serveProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = servePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        _serveProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _output.WriteLine($"[SERVE ERR] {e.Data}");
        };
        
        _serveProcess.Start();
        _serveProcess.BeginErrorReadLine();
        
        // Set up JSON-RPC using the same constructor as ZeroBuffer.Serve
        // First parameter is for sending (stdin of process), second is for receiving (stdout of process)
        _jsonRpc = new JsonRpc(
            _serveProcess.StandardInput.BaseStream,
            _serveProcess.StandardOutput.BaseStream);
        _jsonRpc.StartListening();
        
        // Give the serve process a moment to start up
        await Task.Delay(500);
    }
    
    public void Dispose()
    {
        try
        {
            _jsonRpc?.Dispose();
        }
        catch { }
        
        if (_serveProcess != null && !_serveProcess.HasExited)
        {
            try
            {
                _serveProcess.Kill();
                _serveProcess.WaitForExit(1000);
            }
            catch { }
            finally
            {
                _serveProcess.Dispose();
            }
        }
    }
}

// Data models for deserializing test scenarios
public class DiscoverResponse
{
    public List<StepInfo> Steps { get; set; } = new();
}

public class StepInfo
{
    public string Type { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}