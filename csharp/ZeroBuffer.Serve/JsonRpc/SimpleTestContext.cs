using System;
using System.Collections.Generic;

namespace ZeroBuffer.Serve.JsonRpc
{
    public class SimpleTestContext : ITestContext
    {
        private readonly Dictionary<string, object> _data = new();
        private string _role = "";
        private string _platform = "";
        private string _scenario = "";
        private string _testRunId = "";

        public string Role => _role;
        public string Platform => _platform;
        public string Scenario => _scenario;
        public string TestRunId => _testRunId;

        public void Initialize(string role, string platform, string scenario, string testRunId)
        {
            _role = role;
            _platform = platform;
            _scenario = scenario;
            _testRunId = testRunId;
        }

        public void Cleanup()
        {
            _data.Clear();
        }

        public void SetData(string key, object value)
        {
            _data[key] = value;
        }

        public T GetData<T>(string key)
        {
            if (!_data.TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException($"Context data '{key}' not found");
            }
            return (T)value;
        }

        public bool TryGetData<T>(string key, out T value)
        {
            if (_data.TryGetValue(key, out var obj))
            {
                try
                {
                    value = (T)obj;
                    return true;
                }
                catch
                {
                    // If cast fails, try to convert
                    if (obj != null && typeof(T) == typeof(string))
                    {
                        value = (T)(object)obj.ToString()!;
                        return true;
                    }
                }
            }
            value = default!;
            return false;
        }
    }
}