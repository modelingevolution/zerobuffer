# Harmony Integration - Critical Design Notes

## How Harmony Actually Works

**IMPORTANT**: This document corrects common misconceptions about Harmony's operation.

### Harmony's Step Routing (THE TRUTH)

Harmony does **intelligent step routing**, NOT broadcasting:

1. **Parse Step**: Extract process context from Gherkin step
   - `"Given the 'writer' process creates buffer"` → process="writer"

2. **Map to Platform**: Use current test's platform combination  
   - For "python/python" test: writer → python, reader → python

3. **Route to Specific Server**: Send command ONLY to the appropriate process
   - Writer steps → writer process only
   - Reader steps → reader process only

### What This Means for Step Definitions

**DO NOT FILTER BY ROLE** in step definitions:

```python
# WRONG - Useless role filtering
@given(r"the '([^']+)' process creates buffer")
async def create_buffer(self, process: str, buffer_name: str):
    if self.context.role != process:  # COMPLETELY USELESS!
        return
    # ... actual implementation

# CORRECT - Just execute what you're told
@given(r"the '([^']+)' process creates buffer")
async def create_buffer(self, process: str, buffer_name: str):
    # Harmony already routed this to the right process
    # Just do what's asked!
    config = BufferConfig(...)
    reader = Reader(buffer_name, config)
```

## Test Execution Scenarios

### Python-Only Testing
- **One Python process** executes all steps sequentially
- **Same step definitions** work without modification
- **No role filtering needed**

### Harmony Orchestration  
- **Multiple Python processes** (one per role)
- **Harmony routes steps** to appropriate processes
- **Same step definitions** work without modification
- **No role filtering needed**

## Common Misconceptions (AVOID THESE)

### ❌ WRONG: "Harmony broadcasts to all processes"
Harmony does intelligent routing based on process context extraction.

### ❌ WRONG: "Role filtering is essential for separation"  
Role filtering is completely unnecessary - Harmony handles separation through routing.

### ❌ WRONG: "Each process needs to know its role"
Processes don't need roles - they just execute whatever steps they receive.

## The Protocol Principle

The same test must work identically in both scenarios:
- **Python-only**: Direct step execution
- **Harmony**: JSON-RPC step execution

This is only possible because:
1. **No role filtering** in step definitions
2. **Harmony's intelligent routing** handles process separation
3. **Step definitions are pure** - they just execute operations

## Key Takeaway

**NEVER ADD ROLE FILTERING** - it breaks the protocol principle and prevents seamless switching between Python-only testing and Harmony orchestration.