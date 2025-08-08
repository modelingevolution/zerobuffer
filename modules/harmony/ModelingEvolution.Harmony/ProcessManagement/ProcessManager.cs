using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.Shared;

namespace ModelingEvolution.Harmony.ProcessManagement;

/// <summary>
/// Manages test process lifecycle
/// </summary>
public class ProcessManager : IProcessManager, IDisposable
{
    private readonly MultiprocessConfiguration _configuration;
    private readonly ILogger<ProcessManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, ProcessInfo> _processes = new();
    
    public ProcessManager(MultiprocessConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ProcessManager>();
    }
    
    public async Task StartProcessAsync(string processName, string platform, CancellationToken cancellationToken = default)
    {
        await StartProcessAsync(processName, platform, 0, 0, cancellationToken);
    }

    public async Task StartProcessAsync(string processName, string platform, int hostPid, int featureId, CancellationToken cancellationToken = default)
    {
        var key = $"{processName}:{platform}";
        
        if (_processes.ContainsKey(key))
        {
            _logger.LogDebug("Process {Key} already running", key);
            return;
        }
        
        if (!_configuration.Platforms.TryGetValue(platform, out var platformConfig))
        {
            throw new InvalidOperationException($"Platform '{platform}' not configured");
        }
        
        _logger.LogInformation("Starting process {ProcessName} on platform {Platform} with HostPid={HostPid}, FeatureId={FeatureId}", 
            processName, platform, hostPid, featureId);
        

        var startInfo = new ProcessStartInfo
        {
            FileName = platformConfig.Executable,
            Arguments = platformConfig.Arguments,
            WorkingDirectory = platformConfig.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        // Add environment variables for resource isolation
        foreach (var env in platformConfig.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[env.Key] = env.Value;
        }
        
        // Pass Host PID and Feature ID as environment variables
        if (hostPid > 0)
        {
            startInfo.EnvironmentVariables["HARMONY_HOST_PID"] = hostPid.ToString();
        }
        if (featureId > 0)
        {
            startInfo.EnvironmentVariables["HARMONY_FEATURE_ID"] = featureId.ToString();
        }

        if (!File.Exists(startInfo.FileName))
            throw new Exception("Executable not exists! " + startInfo.FileName);
        if(!Directory.Exists(startInfo.WorkingDirectory))
            throw new Exception("Working directory not exists! " + startInfo.WorkingDirectory);

        // Create connection using JsonRpcProcessConnection (which starts the process)
        var connection = new JsonRpcProcessConnection(
            processName, 
            platform, 
            startInfo, 
            _loggerFactory.CreateLogger<JsonRpcProcessConnection>());
            
        // Create strongly-typed client
        var client = connection.CreateServoClient();
        
        // Health check (parameterless ping)
        var healthResult = await client.HealthAsync(cancellationToken);
        if (!healthResult)
        {
            throw new InvalidOperationException($"Process {processName} health check failed");
        }
        
        // Initialize
        if (hostPid > 0 && featureId > 0)
        {
            var initParams = new InitializeRequest(
                Role: processName,
                Platform: platform,
                Scenario: "test", // This should come from context
                HostPid: hostPid,
                FeatureId: featureId
            );
            await client.InitializeAsync(initParams, cancellationToken);
        }
        
        _processes[key] = new ProcessInfo
        {
            Connection = connection
        };
        
        // Wait for process to be ready
        await Task.Delay(_configuration.ProcessInitializationDelayMs, cancellationToken);
        
        _logger.LogInformation("Process {ProcessName} on platform {Platform} started successfully", processName, platform);
    }
    
    public async Task StopProcessAsync(string processName)
    {
        var toStop = _processes
            .Where(kvp => kvp.Key.StartsWith($"{processName}:"))
            .ToList();
        
        foreach (var kvp in toStop)
        {
            await StopProcessInternalAsync(kvp.Key, kvp.Value);
        }
    }
    
    public async Task StopAllProcessesAsync()
    {
        var tasks = _processes.Select(kvp => StopProcessInternalAsync(kvp.Key, kvp.Value));
        await Task.WhenAll(tasks);
        _processes.Clear();
    }
    
    public Task<bool> IsProcessRunningAsync(string processName)
    {
        var running = _processes.Any(kvp => 
            kvp.Key.StartsWith($"{processName}:") && 
            kvp.Value.Connection.IsConnected);
        
        return Task.FromResult(running);
    }
    
    public IProcessConnection GetConnection(string processName)
    {
        var entry = _processes.FirstOrDefault(kvp => kvp.Key.StartsWith($"{processName}:"));
        if (entry.Value == null)
        {
            throw new InvalidOperationException($"No connection for process '{processName}'");
        }
        
        return entry.Value.Connection;
    }
    
    private async Task StopProcessInternalAsync(string key, ProcessInfo info)
    {
        _logger.LogInformation("Stopping process {Key}", key);
        
        try
        {
            // JsonRpcProcessConnection.Dispose() handles process cleanup
            info.Connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping process {Key}", key);
        }
    }
    
    public void Dispose()
    {
        StopAllProcessesAsync().GetAwaiter().GetResult();
    }
    
    private class ProcessInfo
    {
        public JsonRpcProcessConnection Connection { get; init; } = null!;
    }
}

// ProcessConnection class removed - using JsonRpcProcessConnection instead