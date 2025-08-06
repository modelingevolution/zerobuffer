using System;
using Microsoft.Extensions.Logging;

namespace ZeroBuffer.Tests.Services
{
    public class BufferNamingService : IBufferNamingService
    {
        private readonly ILogger<BufferNamingService> _logger;
        private static int _testCounter = 0;

        public BufferNamingService(ILogger<BufferNamingService> logger)
        {
            _logger = logger;
        }

        public string GetUniqueBufferName(string baseName)
        {
            // First try environment variables (set by Harmony)
            var harmonyPid = Environment.GetEnvironmentVariable("HARMONY_HOST_PID");
            var harmonyFeatureId = Environment.GetEnvironmentVariable("HARMONY_FEATURE_ID");
            
            if (!string.IsNullOrEmpty(harmonyPid) && !string.IsNullOrEmpty(harmonyFeatureId))
            {
                // Running under Harmony - use provided values for resource isolation
                var harmonyName = $"{baseName}_{harmonyPid}_{harmonyFeatureId}";
                _logger.LogDebug("Created Harmony buffer name: {BufferName} (PID: {Pid}, FeatureID: {FeatureId})", 
                    harmonyName, harmonyPid, harmonyFeatureId);
                return harmonyName;
            }
            
            // Running standalone - use process ID and an incrementing counter for uniqueness
            var pid = Environment.ProcessId;
            var testId = System.Threading.Interlocked.Increment(ref _testCounter);
            
            var standaloneName = $"{baseName}_{pid}_{testId}";
            _logger.LogDebug("Created standalone buffer name: {BufferName} (PID: {Pid}, TestID: {TestId})", 
                standaloneName, pid, testId);
            return standaloneName;
        }
    }
}