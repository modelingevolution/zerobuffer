using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using ModelingEvolution.Harmony.Core;

namespace ModelingEvolution.Harmony.ProcessManagement;

/// <summary>
/// Manages test process lifecycle
/// </summary>
public class ProcessManager : IProcessManager, IDisposable
{
    private readonly MultiprocessConfiguration _configuration;
    private readonly ILogger<ProcessManager> _logger;
    private readonly Dictionary<string, ProcessInfo> _processes = new();
    
    public ProcessManager(MultiprocessConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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
        
        var process = new Process { StartInfo = startInfo };
        process.Start();
        
        var connection = new ProcessConnection(processName, platform, process);
        await connection.InitializeAsync(hostPid, featureId, cancellationToken);
        
        _processes[key] = new ProcessInfo
        {
            Process = process,
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
            !kvp.Value.Process.HasExited);
        
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
            info.Connection.Dispose();
            
            if (!info.Process.HasExited)
            {
                info.Process.Kill();
                await info.Process.WaitForExitAsync();
            }
            
            info.Process.Dispose();
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
        public Process Process { get; init; } = null!;
        public ProcessConnection Connection { get; init; } = null!;
    }
}

/// <summary>
/// JSON-RPC connection to a test process
/// </summary>
public class ProcessConnection : IProcessConnection, IDisposable
{
    private readonly Process _process;
    private JsonRpc? _rpc;
    
    public string ProcessName { get; }
    public string Platform { get; }
    public bool IsConnected => _rpc != null && !_process.HasExited;
    
    public ProcessConnection(string processName, string platform, Process process)
    {
        ProcessName = processName;
        Platform = platform;
        _process = process;
    }
    
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(0, 0, cancellationToken);
    }

    public async Task InitializeAsync(int hostPid, int featureId, CancellationToken cancellationToken)
    {
        _rpc = new JsonRpc(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream);
        _rpc.StartListening();
        
        // Initialize process with resource isolation parameters
        var initParams = new { hostPid, featureId };
        var healthResult = await InvokeAsync<bool>("health", initParams, cancellationToken);
        if (!healthResult)
        {
            throw new InvalidOperationException("Process health check failed");
        }
        
        // Send initialization message with host PID and feature ID for resource isolation
        if (hostPid > 0 && featureId > 0)
        {
            await InvokeAsync<bool>("initialize", initParams, cancellationToken);
        }
    }
    
    public async Task<T> InvokeAsync<T>(string method, object parameters, CancellationToken cancellationToken = default)
    {
        if (_rpc == null)
        {
            throw new InvalidOperationException("Connection not initialized");
        }
        
        return await _rpc.InvokeWithCancellationAsync<T>(method, [parameters], cancellationToken);
    }
    
    public void Dispose()
    {
        _rpc?.Dispose();
    }
}