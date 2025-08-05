namespace ZeroBuffer.Serve.JsonRpc;

public class InitializeRequest
{
    public string Role { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public string TestRunId { get; set; } = string.Empty;
}

public class InitializeResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Version { get; set; } = "1.0.0";
    public List<string> Capabilities { get; set; } = new();
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