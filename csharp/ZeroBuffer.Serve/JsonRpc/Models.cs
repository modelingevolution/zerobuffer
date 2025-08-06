namespace ZeroBuffer.Serve.JsonRpc;

public class HealthRequest
{
    public int HostPid { get; set; }
    public int FeatureId { get; set; }
}

public class InitializeRequest
{
    // Harmony ProcessManager parameters
    public int HostPid { get; set; }
    public int FeatureId { get; set; }
    
    // Test context parameters
    public string Role { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;

    public string TestRunId => $"{HostPid}_{FeatureId}";
}


public class StepRequest
{
    public string StepType { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public TableData? Table { get; set; }
}

public class StepResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public List<LogEntry> Logs { get; set; } = new();
}

public class TableData
{
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
}

public class LogEntry
{
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
}

public class DiscoverResponse
{
    public List<StepInfo> Steps { get; set; } = new();
}

public class StepInfo
{
    public string Type { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}