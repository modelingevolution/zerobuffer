# Context Passing Analysis for C++ Servo Integration with Harmony

## Executive Summary

Context passing in Harmony is a critical feature for maintaining state between test steps within a scenario. Currently, the C++ servo **does NOT implement Context passing**, which breaks test scenarios that rely on shared state.

## How Context Works in Harmony

### 1. Context Flow in Harmony Orchestration

```
ScenarioExecution.ExecuteAsync()
├── Initialize context as empty ImmutableDictionary
├── For each step in scenario:
│   ├── Pass current context to StepExecutor
│   ├── StepExecutor creates StepRequest with context
│   ├── Servo executes step with context
│   ├── Servo returns StepResponse with (potentially modified) context
│   └── Update context = response.Context for next step
└── Context accumulates throughout scenario
```

### 2. Key Code Points in Harmony

#### ScenarioExecution.cs (lines 45, 71, 113)
```csharp
// Initialize
ImmutableDictionary<string,string> context = ImmutableDictionary<string, string>.Empty;

// After each step
var result = await stepExecutor.ExecuteStepAsync(step, Platforms, context, cancellationToken);
context = result.Context;  // <-- Context is updated from response
```

#### StepExecutor.cs (lines 135, 163)
```csharp
// Send context to servo
Context: context.ToImmutableDictionary(),

// Receive updated context from servo
Context: response.Context ?? ImmutableDictionary<string, string>.Empty,
```

### 3. Context vs Parameters

| Aspect | Parameters | Context |
|--------|------------|---------|
| **Source** | From Gherkin step definition | From previous step responses |
| **Lifetime** | Single step | Entire scenario |
| **Mutability** | Read-only in step | Can be modified and returned |
| **Purpose** | Step configuration | Shared state between steps |
| **Example** | `bufferSize: "1024"` | `bufferId: "test-123"` |

## Current C++ Servo Implementation Gaps

### What's Missing:
1. **Context extraction from StepRequest** - Currently ignored
2. **Context storage during step execution** - Not stored
3. **Context modification by steps** - No API for steps to update
4. **Context return in StepResponse** - Always returns null
5. **Context isolation between scenarios** - Not implemented

### Current main_v2.cpp Implementation (line 329)
```cpp
// Parameters are extracted and stored
if (!stepParameters.is_null() && stepParameters.is_object()) {
    for (auto& [key, value] : stepParameters.items()) {
        g_testContext.setProperty("param:" + key, value);
    }
}
// BUT Context is completely ignored!
```

## How Context Should Be Used in Tests

### Example Scenario: Buffer Creation and Verification
```gherkin
Scenario: Create buffer and verify across steps
  Given the reader process creates buffer with size 1024
  # Step returns context: { "bufferId": "buffer_12345", "size": "1024" }
  
  When the writer process connects to the buffer
  # Step receives context from previous step
  # Uses bufferId to connect: context["bufferId"]
  # Returns updated context: { ...prev, "writerConnected": "true" }
  
  Then the buffer should have correct configuration
  # Receives accumulated context with bufferId, size, writerConnected
  # Can verify all accumulated state
```

### Integration Test Pattern
```csharp
// Step 1: Initialize and return context
var step1Response = await ExecuteStep(step1Request, 
    Context: ImmutableDictionary<string,string>.Empty);
step1Response.Context.Should().ContainKey("bufferId");

// Step 2: Use context from step 1
var step2Response = await ExecuteStep(step2Request,
    Context: step1Response.Context);  // Pass previous context
step2Response.Context.Should().ContainKey("bufferId");  // Original preserved
step2Response.Context.Should().ContainKey("writerPid");  // New data added

// Step 3: Accumulated context
var step3Response = await ExecuteStep(step3Request,
    Context: step2Response.Context);
// Has access to all accumulated context
```

## Required Implementation for C++ Servo

### 1. Context Storage in TestContext
```cpp
// test_context.h
private:
    std::unordered_map<std::string, std::string> context_;  // Separate from properties
    
public:
    void setContextValue(const std::string& key, const std::string& value);
    std::string getContextValue(const std::string& key) const;
    bool hasContextValue(const std::string& key) const;
    json getAllContext() const;  // For returning in StepResponse
    void setContext(const json& context);  // For receiving from StepRequest
    void clearContext();  // Reset on new scenario
```

### 2. Context Extraction in main_v2.cpp
```cpp
// In executeStep handler
json stepContext;
if (stepRequest.contains("Context")) {
    stepContext = stepRequest["Context"];
} else if (stepRequest.contains("context")) {
    stepContext = stepRequest["context"];
}

// Store context for step to access
if (!stepContext.is_null() && stepContext.is_object()) {
    g_testContext.setContext(stepContext);
}
```

### 3. Context Return in StepResponse
```cpp
// After step execution
json contextToReturn = g_testContext.getAllContext();

// In response
response["result"] = {
    {"success", success},
    {"error", error},
    {"logs", logs},
    {"Context", contextToReturn}  // Return accumulated/modified context
};
```

### 4. Context Modification API for Steps
```cpp
// In step implementation
void some_step_implementation(TestContext& ctx, const std::vector<std::string>& params) {
    // Access incoming context
    std::string bufferId = ctx.getContextValue("bufferId");
    
    // Do work...
    
    // Update context for next steps
    ctx.setContextValue("writerPid", std::to_string(getpid()));
    ctx.setContextValue("connectionTime", getCurrentTimestamp());
}
```

## Testing Context Implementation

### Test 1: Context Accumulation
```csharp
[Fact]
public async Task Context_ShouldAccumulateAcrossSteps()
{
    // Step 1: Set initial context
    var step1 = new StepRequest(..., 
        Context: ImmutableDictionary<string,string>.Empty
            .Add("key1", "value1"));
    var response1 = await Execute(step1);
    response1.Context.Should().ContainKey("key1");
    
    // Step 2: Previous context plus new
    var step2 = new StepRequest(...,
        Context: response1.Context.Add("key2", "value2"));
    var response2 = await Execute(step2);
    response2.Context.Should().ContainKey("key1");  // Preserved
    response2.Context.Should().ContainKey("key2");  // Added
    
    // Step 3: Can modify existing
    var step3 = new StepRequest(...,
        Context: response2.Context.SetItem("key1", "modified"));
    var response3 = await Execute(step3);
    response3.Context["key1"].Should().Be("modified");
}
```

### Test 2: Context Isolation Between Scenarios
```csharp
[Fact]
public async Task Context_ShouldBeIsolatedBetweenScenarios()
{
    // Scenario 1
    await Initialize("scenario1");
    var step1 = new StepRequest(..., 
        Context: ImmutableDictionary.Create<string,string>()
            .Add("scenario", "1"));
    var response1 = await Execute(step1);
    
    // Scenario 2 - new initialization
    await Initialize("scenario2");
    var step2 = new StepRequest(...,
        Context: ImmutableDictionary.Create<string,string>()
            .Add("scenario", "2"));
    var response2 = await Execute(step2);
    
    // Context from scenario 1 should not leak
    response2.Context.Should().NotContainKey("scenario1Data");
    response2.Context["scenario"].Should().Be("2");
}
```

### Test 3: Context vs Parameters
```csharp
[Fact]
public async Task Context_AndParameters_ShouldBeIndependent()
{
    var step = new StepRequest(...,
        Parameters: ImmutableDictionary.Create<string,string>()
            .Add("param1", "value1"),
        Context: ImmutableDictionary.Create<string,string>()
            .Add("ctx1", "value2"));
    
    var response = await Execute(step);
    
    // Both should be accessible but separate
    // Parameters are step-specific configuration
    // Context is shared state
    response.Context.Should().ContainKey("ctx1");
    response.Context.Should().NotContainKey("param1");
}
```

## Implementation Priority

### High Priority (Required for Harmony Compliance)
1. Extract Context from StepRequest
2. Store Context in TestContext (separate from Parameters)
3. Return Context in StepResponse
4. Clear Context on initialize (scenario isolation)

### Medium Priority (Enhanced Functionality)
1. Allow steps to modify Context
2. Merge incoming Context with existing (accumulation)
3. Context validation/sanitization

### Low Priority (Nice to Have)
1. Context size limits
2. Context serialization optimization
3. Context debugging/logging

## Harmony's Current Bug/Issue

Looking at `StepExecutor.CombineResults()` (lines 180, 193), there's a problem:
```csharp
Context: ImmutableDictionary<string, string>.Empty,  // Always returns empty!
```

When combining results from multiple processes (broadcast), Harmony currently **discards all Context** and returns empty. This means:
1. Broadcast steps lose all context
2. Context from multiple processes isn't merged
3. This might be intentional (which context to keep?) but seems like a bug

## Conclusion

Context passing is essential for Harmony test scenarios to maintain state between steps. The C++ servo must implement:
1. Context extraction from StepRequest
2. Context storage separate from Parameters
3. Context return in StepResponse
4. Context reset on scenario initialization

Without this, any test scenario that relies on passing data between steps will fail.