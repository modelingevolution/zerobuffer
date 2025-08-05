# Feature File Naming Convention Changes

## Overview

The feature files need to be updated to reflect the process-based architecture of the Multiprocess Test Runner. This change shifts from platform-specific steps to process-role-based steps.

## Key Changes

### 1. From Platform References to Process References

**Before:**
```gherkin
Given the reader is 'csharp'
And the writer is 'python'
```

**After:**
```gherkin
Given the 'reader' process is configured
And the 'writer' process is configured
```

### 2. Process Context in Every Step

**Before:**
```gherkin
When create buffer 'test' with default config
Then read frame should succeed
```

**After:**
```gherkin
When the 'writer' process creates buffer 'test' with default config
Then the 'reader' process should read frame successfully
```

### 3. Remove Platform-Specific Setup

**Before:**
```gherkin
Background:
  Given the test mode is configured
```

**After:**
```gherkin
Background:
  Given the test environment is initialized
  And all processes are ready
```

## Rationale

1. **Platform Independence**: Steps describe what processes do, not which platform they use
2. **Clearer Intent**: Each step clearly identifies which process performs the action
3. **Better Test Generation**: The runner can map process roles to platforms systematically
4. **Improved Readability**: Scenarios read more like natural language descriptions

## Step Pattern Templates

### Given Steps (Setup)
- `Given the '{process}' process is configured`
- `Given the '{process}' process has {configuration}`
- `Given '{process}' starts with {parameters}`

### When Steps (Actions)
- `When the '{process}' process {action}`
- `When '{process}' performs {operation}`
- `When the '{process}' process {action} with {parameters}`

### Then Steps (Assertions)
- `Then the '{process}' process should {expectation}`
- `Then '{process}' observes {result}`
- `Then the '{process}' process {verification}`

## Example Transformation

### Original Scenario:
```gherkin
Scenario: Test 1.1 - Simple Write-Read Cycle
  Given the reader is 'csharp'
  And create buffer 'test' with default config
  When the writer is 'python'
  And connect to buffer 'test'
  And write single frame with text 'Hello'
  Then the reader is 'csharp'
  And read single frame
  And frame text should be 'Hello'
```

### Transformed Scenario:
```gherkin
Scenario: Test 1.1 - Simple Write-Read Cycle
  Given the 'reader' process creates buffer 'test' with default config
  When the 'writer' process connects to buffer 'test'
  And the 'writer' process writes single frame with text 'Hello'
  Then the 'reader' process reads single frame
  And the 'reader' process should receive frame with text 'Hello'
```

## Migration Notes

1. **No Platform Names**: Remove all references to 'csharp', 'python', 'cpp' from steps
2. **Process Names**: Use logical role names: 'reader', 'writer', 'server', 'client'
3. **Explicit Context**: Every action should specify which process performs it
4. **State Assertions**: Clearly indicate which process is checking the state

## Benefits

- **Cleaner Tests**: Focus on behavior, not implementation
- **Easier Maintenance**: One scenario works for all platform combinations
- **Better Error Messages**: "writer process failed" vs "python failed"
- **Natural Language**: Reads like a conversation about the system