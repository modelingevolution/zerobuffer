# C++ Servo vs Harmony Schema Differences

This document details the exact schema differences between what the Harmony framework expects and what the C++ servo currently implements.

## Summary

The C++ servo is **partially compatible** with Harmony. While basic communication works, several important fields and features are not implemented.

## 1. Initialize Method

### Harmony Expects (InitializeRequest)
```csharp
{
  "Role": "reader",        // string - Process role (reader/writer/both)
  "Platform": "cpp",       // string - Platform identifier
  "Scenario": "Test123",   // string - Test scenario name
  "HostPid": 12345,        // int - Host process ID
  "FeatureId": 1,          // int - Feature identifier
  "TestRunId": "12345_1"   // string - Computed from HostPid_FeatureId
}
```

### C++ Servo Implementation
- **Accepts**: All fields (doesn't reject them)
- **Processes**: Only looks for optional `testName` field
- **Stores**: ❌ NOTHING - doesn't store any context
- **Returns**: `true` (boolean)

### Issues
- ❌ **Critical**: Role, Platform, Scenario are ignored
- ❌ **Critical**: No test context is maintained
- ❌ HostPid and FeatureId are not stored

## 2. ExecuteStep Method

### Harmony Expects (StepRequest)
```csharp
{
  "Process": "reader",              // Which process should execute
  "StepType": "Given",              // Enum: Given/When/Then
  "Step": "the test environment...", // Step text
  "Parameters": {                   // Step parameters
    "key1": "value1"
  },
  "Context": {                      // Shared context
    "ctx1": "data1"
  },
  "IsBroadcast": false             // Broadcast to all processes
}
```

### C++ Servo Implementation
```cpp
// Only processes:
{
  "stepType": "Given",              // Case-insensitive
  "step": "the test environment..." // Step text
}
// Ignores: Process, Parameters, Context, IsBroadcast
```

### Issues
- ❌ **Critical**: Process field ignored (can't route to correct process)
- ❌ **Critical**: Parameters not passed to steps
- ❌ Context not maintained between steps
- ❌ IsBroadcast not supported

## 3. ExecuteStep Response

### Harmony Expects (StepResponse)
```csharp
{
  "Success": true,           // bool - Step succeeded?
  "Error": null,            // string? - Error message if failed
  "Context": {              // Dictionary - Updated context
    "key": "value"
  },
  "Logs": [                 // List of log entries
    {
      "Timestamp": "2024-01-01T10:00:00Z",
      "Level": 2,           // LogLevel enum (0-6)
      "Message": "Step executed"
    }
  ]
}
```

### C++ Servo Returns
```json
{
  "success": true,          // lowercase
  "error": null,
  "data": {},              // Always empty, extra field
  "logs": [                // lowercase
    {
      "Level": "INFO",     // String, not enum
      "Message": "Step executed"
      // Missing: Timestamp
    }
  ]
}
```

### Issues
- ⚠️ Field naming: lowercase vs PascalCase
- ❌ `data` field is extra (should be `Context`)
- ❌ Context not implemented
- ❌ Logs missing Timestamp
- ❌ Log Level is string ("INFO") not enum (2)

## 4. Discover Method

### Harmony Expects (DiscoverResponse)
```csharp
{
  "Steps": [                // PascalCase
    {
      "Type": "Given",      // PascalCase
      "Pattern": "the test environment is initialized"
    }
  ]
}
```

### C++ Servo Returns
```json
{
  "steps": [               // lowercase
    {
      "type": "Given",     // lowercase
      "pattern": "the test environment is initialized"
    }
  ],
  "capabilities": {        // Extra field
    "timeout": true,
    "contentLength": true,
    "logging": true
  }
}
```

### Issues
- ⚠️ Field naming inconsistency
- ❌ Extra `capabilities` field (not in contract)

## 5. Field Naming Convention

| Method | Harmony (C#) | C++ Servo | Match |
|--------|-------------|-----------|-------|
| Initialize | PascalCase | accepts any | ⚠️ |
| ExecuteStep Request | PascalCase | camelCase | ❌ |
| ExecuteStep Response | PascalCase | camelCase | ❌ |
| Discover | PascalCase | camelCase | ❌ |

## Required Changes for Full Compatibility

### Priority 1: Critical Functionality (🔴 HIGH)
1. **Store initialization context** in TestContext:
   - Role, Platform, Scenario, HostPid, FeatureId
   - Make available to step implementations

2. **Support Parameters in StepRequest**:
   - Pass to step implementations
   - Allow steps to access parameters

3. **Support Process field**:
   - Route steps to correct process context
   - Validate process matches initialization role

### Priority 2: Schema Compliance (🟡 MEDIUM)
1. **Fix field naming to PascalCase**:
   - All response fields should use PascalCase
   - Accept both in requests for compatibility

2. **Add Timestamp to log entries**:
   - ISO 8601 format
   - Include in each log entry

3. **Convert Log Level to enum**:
   - 0 = Trace, 1 = Debug, 2 = Information
   - 3 = Warning, 4 = Error, 5 = Critical, 6 = None

### Priority 3: Extended Features (🟢 LOW)
1. **Implement Context dictionary**:
   - Maintain between steps
   - Return in StepResponse

2. **Remove or relocate extra fields**:
   - Remove `data` field or rename to `Context`
   - Move `capabilities` to separate method

3. **Support IsBroadcast**:
   - For future multi-process scenarios

## Compatibility Matrix

| Feature | Works Now | After Fix |
|---------|-----------|-----------|
| Basic step execution | ✅ | ✅ |
| Multi-process routing | ❌ | ✅ |
| Step parameters | ❌ | ✅ |
| Context sharing | ❌ | ✅ |
| Structured logging | ⚠️ | ✅ |
| Full Harmony compliance | ❌ | ✅ |

## Migration Path

1. **Phase 1**: Add missing fields without breaking existing
   - Add initialization context storage
   - Support Parameters in steps
   - Add Timestamp to logs

2. **Phase 2**: Fix naming conventions
   - Support both camelCase and PascalCase
   - Gradually migrate to PascalCase

3. **Phase 3**: Full compliance
   - Remove extra fields
   - Implement Context
   - Add process routing