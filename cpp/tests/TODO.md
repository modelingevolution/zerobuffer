# ZeroBuffer C++ Testing - Status and TODO

## ✅ Completed

### Infrastructure
- ✅ Step Registry with pattern matching for Gherkin steps
- ✅ Test Context for state management across steps
- ✅ JSON-RPC server (zerobuffer-serve) for Harmony integration
- ✅ Google Test generation from feature files
- ✅ harmony-cpp-gen dotnet tool for test generation
- ✅ Logging system with environment variable control
- ✅ SpecFlow-like output with Gherkin keywords

### Step Definitions
- ✅ Basic Communication steps (partial - Test 1.1 working)

### Build System
- ✅ CMake integration for generated tests
- ✅ test.sh script for local testing
- ✅ build.sh includes tests by default

## 📝 TODO - Remaining Step Definitions

### Priority 1 - Core Features
- [ ] Complete BasicCommunication steps (Tests 1.2 - 1.5)
- [ ] ProcessLifecycle steps
- [ ] ErrorHandling steps
- [ ] EdgeCases steps

### Priority 2 - Advanced Features
- [ ] CorruptionDetection steps
- [ ] Synchronization steps
- [ ] SystemResources steps
- [ ] PlatformSpecific steps

### Priority 3 - Performance & Stress
- [ ] PerformanceMonitoring steps
- [ ] AdvancedErrorHandling steps
- [ ] StressTests steps
- [ ] ProtocolCompliance steps
- [ ] DuplexChannel steps
- [ ] Benchmarks steps

## 📋 Implementation Guide

### Adding New Step Definitions

1. Create new step file in `step_definitions/` (e.g., `edge_cases_steps.cpp`)
2. Implement the register function:
```cpp
void registerEdgeCasesSteps() {
    auto& registry = StepRegistry::getInstance();
    
    registry.registerStep(
        "pattern here",
        [](TestContext& ctx, const std::vector<std::string>& params) {
            // Implementation
        }
    );
}
```

3. Add registration call to `step_registry.cpp::registerAllSteps()`
4. Add source file to `serve/CMakeLists.txt`
5. Add source file to `tests/generated/CMakeLists.txt`
6. Rebuild and test

### Testing New Steps

```bash
# Local testing
./test.sh <test-number>

# With debug output
ZEROBUFFER_LOG_LEVEL=DEBUG ./test.sh <test-number>

# Cross-platform via Harmony
cd ..
./test.sh cpp <test-number>
```

## 🎯 Next Immediate Tasks

1. **Complete BasicCommunication** - Implement remaining steps for Tests 1.2-1.5
2. **Add ProcessLifecycle** - Critical for multi-process scenarios
3. **Add ErrorHandling** - Essential for robust testing

## 📊 Progress Tracking

| Feature | Steps Defined | Tests Passing | Notes |
|---------|--------------|---------------|--------|
| BasicCommunication | 12/~20 | 1/5 | Test 1.1 works |
| ProcessLifecycle | 0/? | 0/? | Not started |
| ErrorHandling | 0/? | 0/? | Not started |
| EdgeCases | 0/? | 0/? | Not started |
| ... | ... | ... | ... |

## 🔧 Known Issues

1. Clock skew warnings in WSL - harmless but annoying
2. Long build times when all tests are included (~2 minutes)
3. Some complex multi-process scenarios may need special handling

## 📚 Resources

- Feature files: `../ZeroBuffer.Harmony.Tests/Features/`
- Step examples: `step_definitions/basic_communication_steps.cpp`
- Test generator: `harmony-cpp-gen --help`
- Logging control: Set `ZEROBUFFER_LOG_LEVEL` environment variable