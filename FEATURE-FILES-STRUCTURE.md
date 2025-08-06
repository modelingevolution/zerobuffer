# Feature Files Structure

This document describes the organization of Gherkin feature files in the ZeroBuffer project.

## Overview

The ZeroBuffer project uses Gherkin feature files to define cross-platform integration tests. These tests are executed using the Harmony test framework, which orchestrates test execution across different language implementations (C#, C++, Python).

## Directory Structure

### Source of Truth: `/ZeroBuffer.Harmony.Tests/Features/`

This is the primary location for all feature files. All test scenarios should be defined and maintained here.

**Feature files include:**
- `BasicCommunication.feature` - Fundamental read/write operations
- `ProcessLifecycle.feature` - Process crash detection and recovery
- `EdgeCases.feature` - Boundary conditions and edge cases
- `ErrorHandling.feature` - Error conditions and recovery
- `Performance.feature` - Performance and throughput tests
- `Benchmarks.feature` - Performance benchmarking scenarios
- `DuplexChannel.feature` - Bidirectional communication tests
- `DuplexAdvanced.feature` - Advanced duplex scenarios
- `Initialization.feature` - Buffer initialization and resource management
- `StressTests.feature` - Long-running stress tests
- `Synchronization.feature` - Concurrent operations and data integrity
- `PlatformSpecific.feature` - Platform-specific test scenarios

### Automated Copy: `/csharp/ZeroBuffer.Tests/Features/`

Feature files in this location are automatically copied from `/ZeroBuffer.Harmony.Tests/Features/` during the build process. This ensures that the ZeroBuffer.Tests project always has the latest feature definitions for in-process testing.

**Do not edit files in this location directly** - they will be overwritten during the next build.

## Build Process

### ZeroBuffer.Harmony.Tests

1. Feature files are read from the `/Features` directory
2. The TestGeneratorConsole generates xUnit test classes based on the feature files
3. Generated tests are placed in the `/Generated` directory
4. Tests execute by orchestrating processes defined in `harmony-config.json`

### ZeroBuffer.Tests

1. Pre-build task copies all feature files from `/ZeroBuffer.Harmony.Tests/Features/`
2. Copied files are placed in `/csharp/ZeroBuffer.Tests/Features/`
3. SpecFlow generates test classes from the feature files
4. The Tests project implements the step definitions for in-process testing

## Feature File Convention

As of the latest update, feature files follow a process-based convention rather than platform-specific steps:

### Old Convention (Deprecated)
```gherkin
Given the reader is 'csharp'
And the writer is 'python'
```

### New Convention
```gherkin
Given the 'reader' process creates buffer 'test-buffer' with default config
When the 'writer' process connects to buffer 'test-buffer'
```

The platform selection is now handled by the test configuration (`harmony-config.json`) rather than being specified in the feature files.

## Adding New Features

1. Create or modify feature files in `/ZeroBuffer.Harmony.Tests/Features/`
2. Follow the process-based convention (use 'reader' and 'writer' process names)
3. Implement corresponding step definitions in:
   - `/csharp/ZeroBuffer.Tests/StepDefinitions/` for in-process tests
   - Process-specific implementations for Harmony cross-process tests
4. Build the solution - feature files will be automatically copied and tests generated

## Important Notes

- Always maintain feature files in `/ZeroBuffer.Harmony.Tests/Features/`
- Never edit feature files in `/csharp/ZeroBuffer.Tests/Features/` directly
- Use the process-based convention for all new scenarios
- Ensure step definitions match the exact text in feature files (including grammar)