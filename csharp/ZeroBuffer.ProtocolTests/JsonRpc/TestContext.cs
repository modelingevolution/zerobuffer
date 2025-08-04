using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ZeroBuffer.ProtocolTests.JsonRpc
{
    /// <summary>
    /// Represents an active test instance
    /// </summary>
    public class TestContext : IDisposable
    {
        private readonly IProtocolTest _test;
        private readonly string _role;
        private readonly string _bufferName;
        private readonly ConcurrentBag<string> _executedSteps = new();
        private readonly CancellationTokenSource _cts = new();
        
        // Test state
        private Reader? _reader;
        private Writer? _writer;
        private byte[]? _lastWrittenData;
        private byte[]? _lastReadFrameData;
        private ulong _lastReadFrameSequence;
        private bool _hasLastReadFrame;
        private int _exitCode = -1;

        public TestContext(IProtocolTest test, string role, string bufferName)
        {
            _test = test;
            _role = role;
            _bufferName = bufferName;
        }

        public int ExitCode => _exitCode;

        public async Task<object?> ExecuteStepAsync(string stepName, Dictionary<string, object>? args)
        {
            _executedSteps.Add($"{stepName}({JsonSerializer.Serialize(args)})");
            
            return stepName.ToLowerInvariant() switch
            {
                // Buffer operations
                "createbuffer" => CreateBuffer(args),
                "connecttobuffer" => ConnectToBuffer(args),
                "closebuffer" => CloseBuffer(),
                
                // Write operations
                "writemetadata" => WriteMetadata(args),
                "writeframe" => await WriteFrameAsync(args),
                
                // Read operations
                "readmetadata" => ReadMetadata(),
                "readframe" => await ReadFrameAsync(args),
                
                // Verification operations
                "verifymetadata" => VerifyMetadata(args),
                "verifyframe" => VerifyFrame(args),
                
                // State queries
                "iswriterconnected" => IsWriterConnected(args),
                "isreaderconnected" => IsReaderConnected(args),
                
                _ => throw new NotSupportedException($"Step '{stepName}' is not supported")
            };
        }

        private object CreateBuffer(Dictionary<string, object>? args)
        {
            if (_reader != null)
                throw new InvalidOperationException("Buffer already created");

            var metadataSize = GetArg<int>(args, "metadataSize");
            var payloadSize = GetArg<int>(args, "payloadSize");
            
            var config = new BufferConfig(metadataSize, payloadSize);
            _reader = new Reader(_bufferName, config);
            
            return new { created = true, bufferName = _bufferName };
        }

        private object ConnectToBuffer(Dictionary<string, object>? args)
        {
            if (_writer != null)
                throw new InvalidOperationException("Already connected to buffer");

            _writer = new Writer(_bufferName);
            return new { connected = true };
        }

        private object CloseBuffer()
        {
            _reader?.Dispose();
            _reader = null;
            _writer?.Dispose();
            _writer = null;
            return new { closed = true };
        }

        private object WriteMetadata(Dictionary<string, object>? args)
        {
            if (_writer == null)
                throw new InvalidOperationException("Not connected as writer");

            var data = GetBytesArg(args, "data");
            _writer.SetMetadata(data);
            
            return new { written = true, size = data.Length };
        }

        private async Task<object> WriteFrameAsync(Dictionary<string, object>? args)
        {
            if (_writer == null)
                throw new InvalidOperationException("Not connected as writer");

            var data = GetBytesArg(args, "data");
            var sequence = GetArg<ulong>(args, "sequence", 0);
            
            _lastWrittenData = data;
            _writer.WriteFrame(data);
            
            // Small delay to ensure frame is written
            await Task.Delay(10);
            
            return new { written = true, size = data.Length, sequence };
        }

        private object ReadMetadata()
        {
            if (_reader == null)
                throw new InvalidOperationException("Not connected as reader");

            var metadata = _reader.GetMetadata();
            return new { data = Convert.ToBase64String(metadata), size = metadata.Length };
        }

        private async Task<object> ReadFrameAsync(Dictionary<string, object>? args)
        {
            if (_reader == null)
                throw new InvalidOperationException("Not connected as reader");

            var timeoutMs = GetArg<int>(args, "timeoutMs", 5000);
            
            var frame = _reader.ReadFrame(TimeSpan.FromMilliseconds(timeoutMs));
            
            if (!frame.IsValid)
                throw new TimeoutException("Failed to read frame within timeout");
            
            _lastReadFrameData = frame.ToArray();
            _lastReadFrameSequence = frame.Sequence;
            _hasLastReadFrame = true;
            
            return new 
            { 
                data = Convert.ToBase64String(_lastReadFrameData), 
                size = _lastReadFrameData.Length,
                sequence = _lastReadFrameSequence,
                isValid = true
            };
        }

        private object VerifyMetadata(Dictionary<string, object>? args)
        {
            if (_reader == null)
                throw new InvalidOperationException("Not connected as reader");

            var expected = GetBytesArg(args, "expected");
            var actual = _reader.GetMetadata();
            
            var matches = actual.SequenceEqual(expected);
            if (!matches)
            {
                throw new AssertionException($"Metadata mismatch. Expected {expected.Length} bytes, got {actual.Length} bytes");
            }
            
            return new { verified = true };
        }

        private object VerifyFrame(Dictionary<string, object>? args)
        {
            if (!_hasLastReadFrame || _lastReadFrameData == null)
                throw new InvalidOperationException("No valid frame to verify");

            var expectedData = GetBytesArg(args, "expectedData", null);
            var expectedSequence = GetArg<ulong>(args, "expectedSequence", _lastReadFrameSequence);
            
            if (_lastReadFrameSequence != expectedSequence)
            {
                throw new AssertionException($"Sequence mismatch. Expected {expectedSequence}, got {_lastReadFrameSequence}");
            }
            
            if (expectedData != null)
            {
                if (!_lastReadFrameData.SequenceEqual(expectedData))
                {
                    throw new AssertionException($"Frame data mismatch. Expected {expectedData.Length} bytes, got {_lastReadFrameData.Length} bytes");
                }
            }
            
            return new { verified = true };
        }

        private object IsWriterConnected(Dictionary<string, object>? args)
        {
            if (_reader == null)
                return new { connected = false };

            var timeoutMs = GetArg<int>(args, "timeoutMs", 0);
            var connected = _reader.IsWriterConnected(timeoutMs);
            
            return new { connected };
        }

        private object IsReaderConnected(Dictionary<string, object>? args)
        {
            // This would need to be implemented in the Writer class
            return new { connected = _writer != null };
        }

        private T GetArg<T>(Dictionary<string, object>? args, string key, T defaultValue = default!)
        {
            if (args == null || !args.TryGetValue(key, out var value))
                return defaultValue;

            if (value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText())!;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }

        private byte[] GetBytesArg(Dictionary<string, object>? args, string key, byte[]? defaultValue = null)
        {
            if (args == null || !args.TryGetValue(key, out var value))
            {
                if (defaultValue == null)
                    throw new ArgumentException($"Required argument '{key}' not found");
                return defaultValue;
            }

            if (value is string base64)
            {
                return Convert.FromBase64String(base64);
            }

            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                return Convert.FromBase64String(jsonElement.GetString()!);
            }

            throw new ArgumentException($"Argument '{key}' must be a base64 string");
        }

        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Test {_test.TestId}: {_test.Description}");
            sb.AppendLine($"Role: {_role}");
            sb.AppendLine($"Buffer: {_bufferName}");
            sb.AppendLine($"Steps executed: {_executedSteps.Count}");
            
            if (_exitCode == 0)
                sb.AppendLine("Result: SUCCESS");
            else
                sb.AppendLine($"Result: FAILED (exit code {_exitCode})");
            
            return sb.ToString();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _reader?.Dispose();
            _writer?.Dispose();
            
            if (_exitCode == -1)
                _exitCode = 0; // Success if no explicit failure
        }
    }

    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }
}