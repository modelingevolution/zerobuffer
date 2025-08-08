using ModelingEvolution.Harmony.Shared;

namespace ZeroBuffer.Serve.JsonRpc;

// Use all contracts from ModelingEvolution.Harmony.Shared
// This file is kept for backward compatibility but delegates to shared contracts

// For compatibility - TableData is not in shared contracts yet
public class TableData
{
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
}