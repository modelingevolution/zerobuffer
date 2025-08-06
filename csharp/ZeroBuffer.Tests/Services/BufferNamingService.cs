using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ZeroBuffer.Tests.Services
{
    public class BufferNamingService : IBufferNamingService
    {
        private readonly ILogger<BufferNamingService> _logger;
        private readonly Dictionary<string, string> _nameCache = new();
        private readonly string _testRunId;

        public BufferNamingService(ILogger<BufferNamingService> logger)
        {
            _logger = logger;
            
            // First try environment variables (set by Harmony)
            var harmonyPid = Environment.GetEnvironmentVariable("HARMONY_HOST_PID");
            var harmonyFeatureId = Environment.GetEnvironmentVariable("HARMONY_FEATURE_ID");
            
            if (!string.IsNullOrEmpty(harmonyPid) && !string.IsNullOrEmpty(harmonyFeatureId))
            {
                // Running under Harmony - use provided values for resource isolation
                _testRunId = $"{harmonyPid}_{harmonyFeatureId}";
                _logger.LogDebug("Initialized with Harmony test run ID: {TestRunId}", _testRunId);
            }
            else
            {
                // Running standalone - use process ID and timestamp for uniqueness
                var pid = Environment.ProcessId;
                var timestamp = DateTime.UtcNow.Ticks;
                _testRunId = $"{pid}_{timestamp}";
                _logger.LogDebug("Initialized with standalone test run ID: {TestRunId}", _testRunId);
            }
        }

        public string GetBufferName(string baseName)
        {
            // Return cached name if we've seen this base name before
            if (_nameCache.TryGetValue(baseName, out var cachedName))
            {
                //_logger.LogDebug("Returning cached buffer name: {BufferName} for base name: {BaseName}", cachedName, baseName);
                return cachedName;
            }
            
            // Create new unique name and cache it
            var uniqueName = $"{baseName}_{_testRunId}";
            _nameCache[baseName] = uniqueName;
            
            //_logger.LogDebug("Created and cached buffer name: {BufferName} for base name: {BaseName}", uniqueName, baseName);
            return uniqueName;
        }
    }
}