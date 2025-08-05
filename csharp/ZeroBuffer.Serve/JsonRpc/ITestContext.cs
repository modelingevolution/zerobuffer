namespace ZeroBuffer.Serve.JsonRpc;

public interface ITestContext
{
    string Role { get; }
    string Platform { get; }
    string Scenario { get; }
    string TestRunId { get; }
    
    void Initialize(string role, string platform, string scenario, string testRunId);
    void Cleanup();
    void SetData(string key, object value);
    T GetData<T>(string key);
    bool TryGetData<T>(string key, out T value);
}