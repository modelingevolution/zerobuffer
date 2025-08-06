using System;
using System.Collections.Generic;

namespace ZeroBuffer.Tests
{
    public interface ITestContext
    {
        void SetData<T>(string key, T value);
        T GetData<T>(string key);
        bool TryGetData<T>(string key, out T value);
    }

    public class TestContext : ITestContext
    {
        private readonly Dictionary<string, object> _data = new();

        public void SetData<T>(string key, T value)
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