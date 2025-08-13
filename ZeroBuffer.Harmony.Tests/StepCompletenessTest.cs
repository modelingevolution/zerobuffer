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