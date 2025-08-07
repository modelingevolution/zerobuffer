# C++ ZeroBuffer Testing Infrastructure Implementation Plan

## Overview
Set up core testing infrastructure to enable C++ developers to implement and test ZeroBuffer scenarios following the same workflow as C#/Python developers.

## Goal
Enable a C++ developer to:
1. Read a scenario from feature files
2. Implement step definitions in C++
3. Run single test locally: `cpp/test.sh 1.1`
4. Run with Harmony: `../../test.sh cpp 1.1`
5. Follow a similar README.md guide as C# developers

## Iteration 1: Walking Skeleton - Hardcoded Test 1.1
**Goal**: Establish end-to-end proof that C++ can execute Test 1.1 with real ZeroBuffer

### Tasks
- [ ] Create `tests/poc/test_1_1_hardcoded.cpp` with main()
- [ ] Implement Test 1.1 steps directly:
  ```cpp
  // Step 1: Reader creates buffer
  zerobuffer::Reader reader("simple-test", BufferConfig(4096, 65536));
  
  // Step 2: Writer connects
  zerobuffer::Writer writer("simple-test");
  
  // Step 3: Writer writes data
  std::string message = "Hello, ZeroBuffer!";
  writer.write_frame(message.data(), message.size());
  
  // Step 4: Reader reads and verifies
  auto frame = reader.read_frame(std::chrono::seconds(5));
  assert(frame.valid() && frame.size() == message.size());
  ```
- [ ] Add CMakeLists.txt to build executable
- [ ] Handle exceptions and print clear pass/fail

### Deliverables
- `./build/tests/poc/test_1_1_hardcoded` that passes
- Validates C++ ZeroBuffer integration
- Baseline for refactoring

### Definition of Done
```bash
./test_1_1_hardcoded
# Output: "Test 1.1 - Simple Write-Read Cycle: PASS"
```

---

## Iteration 2: Extract Core Infrastructure
**Goal**: Refactor hardcoded test into reusable infrastructure while keeping it running

### Tasks
- [ ] Create `step_definitions/StepRegistry.h/cpp`:
  - [ ] Singleton pattern for global registry
  - [ ] `registerStep(pattern, handler)` method
  - [ ] `executeStep(text)` with pattern matching
  - [ ] Convert `{word}`, `{string}`, `{int}` to regex
- [ ] Create `step_definitions/TestContext.h/cpp`:
  - [ ] `std::map` for readers/writers by process name
  - [ ] Property storage for test data
  - [ ] Exception capture for error steps
- [ ] Refactor `test_1_1_hardcoded.cpp` to `test_1_1_with_infra.cpp`:
  - [ ] Register 4 steps with patterns
  - [ ] Execute via: `registry.executeStep("Given the reader process...")`
  - [ ] Delete old hardcoded version after new one works

### Deliverables
- Same test passes but using infrastructure
- StepRegistry and TestContext ready for reuse
- Clean separation of concerns

### Definition of Done
```bash
./test_1_1_with_infra
# Output: "Test 1.1 - Simple Write-Read Cycle: PASS"
# Debug: "Matched: Given the {word} process creates buffer {string}..."
```

---

## Iteration 3: JSON-RPC Serve Foundation
**Goal**: Create minimal `zerobuffer-serve` that can execute a single step

### Tasks
- [ ] Create `serve/main.cpp` for `zerobuffer-serve` executable
- [ ] Add nlohmann/json (already in project)
- [ ] Implement basic stdin/stdout loop:
  ```cpp
  std::string line;
  while (std::getline(std::cin, line)) {
      auto request = json::parse(line);
      auto response = handleRequest(request);
      std::cout << response.dump() << std::endl;
  }
  ```
- [ ] Handle only `executeStep` method:
  - [ ] Extract step text from params
  - [ ] Call `StepRegistry::executeStep()`
  - [ ] Return `{success: true/false}` as JSON
- [ ] Link with StepRegistry from Iteration 2
- [ ] Create `test_serve.sh` to validate

### Deliverables
- `./zerobuffer-serve` processes JSON-RPC requests
- Can execute Test 1.1 steps via JSON
- Foundation for Harmony protocol

### Definition of Done
```bash
echo '{"method":"executeStep","params":{"step":"Given the reader process creates buffer simple-test with default configuration"},"id":1}' | ./zerobuffer-serve
# Output: {"id":1,"result":{"success":true}}
```

---

## Iteration 4: Harmony Protocol Compliance
**Goal**: Make `zerobuffer-serve` fully compatible with Harmony orchestrator

### Tasks
- [ ] Add Content-Length header support:
  ```cpp
  // Read: "Content-Length: 123\r\n\r\n{...}"
  size_t readContentLength();
  std::string readJsonBody(size_t length);
  void writeResponse(const json& response);
  ```
- [ ] Implement required JSON-RPC methods:
  - [ ] `health` → return `true`
  - [ ] `initialize` → store test context info
  - [ ] `discover` → return list of registered steps
  - [ ] `cleanup` → reset TestContext between tests
  - [ ] `shutdown` → exit gracefully
- [ ] Add 30-second timeout to executeStep:
  ```cpp
  auto future = std::async(std::launch::async, [&]() {
      return registry.executeStep(step, context);
  });
  if (future.wait_for(30s) == std::future_status::timeout) {
      return timeoutResponse();
  }
  ```
- [ ] Add basic logging to stderr
- [ ] Update Harmony config to include C++ platform

### Deliverables
- Full Harmony compatibility
- Timeout enforcement working
- Can run via `../../test.sh cpp 1.1`

### Definition of Done
```bash
# From zerobuffer root
./test.sh cpp 1.1
# Output: "✓ cpp/cpp | Test 1.1 - Simple Write-Read Cycle"
```

---

## Iteration 5: Developer Experience
**Goal**: Enable C++ developer workflow with local testing and tooling

### Tasks
- [ ] Create `cpp/test.sh` script:
  ```bash
  #!/bin/bash
  TEST_NUM=$1
  # Run specific test via Google Test
  ./build/tests/test_zerobuffer --gtest_filter="*Test${TEST_NUM//./}_*"
  ```
- [ ] Create minimal test generator:
  - [ ] Simple script to wrap Test 1.1 in TEST() macro
  - [ ] No need for full C# generator yet
- [ ] Add Google Test wrapper:
  ```cpp
  TEST(BasicCommunication, Test1_1_SimpleWriteReadCycle) {
      StepRegistry& registry = StepRegistry::getInstance();
      TestContext context;
      
      ASSERT_TRUE(registry.executeStep("Given the reader...", context));
      ASSERT_TRUE(registry.executeStep("When the writer...", context));
      // etc.
  }
  ```
- [ ] Update CMakeLists.txt for test discovery
- [ ] Update README.md with final instructions

### Deliverables
- `./test.sh 1.1` works for local testing
- Clear documentation for C++ developers
- Both test modes (local and Harmony) operational

### Definition of Done
```bash
# Local test
cd cpp
./test.sh 1.1
# Output: "[==========] Running 1 test"
#         "[ PASSED ] 1 test"

# Harmony test  
cd ..
./test.sh cpp 1.1
# Output: "✓ cpp/cpp | Test 1.1 - Simple Write-Read Cycle"
```

---

## Summary

After 5 iterations, we deliver a complete testing infrastructure that enables C++ developers to:
- Write step definitions using patterns
- Test locally with Google Test: `./test.sh 1.1`
- Test with Harmony: `../../test.sh cpp 1.1`
- Follow the same workflow as C#/Python developers

## Key Principles

1. **Walking Skeleton First** - Get Test 1.1 working end-to-end before abstracting
2. **Incremental Refactoring** - Extract infrastructure from working code
3. **Always Runnable** - Each iteration produces executable deliverables
4. **Test Everything** - Clear "Definition of Done" for each iteration
5. **YAGNI** - Build only what's needed for Test 1.1, expand later

## Time Estimates

| Iteration | Duration | Cumulative |
|-----------|----------|------------|
| 1. Walking Skeleton | 0.5 days | 0.5 days |
| 2. Extract Infrastructure | 1.5 days | 2 days |
| 3. JSON-RPC Foundation | 1 day | 3 days |
| 4. Harmony Compliance | 2 days | 5 days |
| 5. Developer Experience | 1 day | 6 days |

**Total: ~6 days** for complete infrastructure with Test 1.1

## Final Deliverables

### For Infrastructure Developer (You)
- ✅ Working Test 1.1 in both modes
- ✅ Proven infrastructure components
- ✅ Integration with Harmony
- ✅ Documentation and examples

### For C++ Developer (Future)
- 📁 `step_definitions/` - Infrastructure to register steps
- 🔧 `zerobuffer-serve` - Harmony integration
- 📜 `test.sh` - Local testing script
- 📚 `README.md` - Step-by-step guide
- ✨ Test 1.1 - Working example to follow

## Dependencies

### Already Available
- ✅ nlohmann/json
- ✅ Google Test
- ✅ ZeroBuffer C++ library

### Need to Add
- ⚠️ None for basic functionality (JSON parsing via nlohmann)
- 📝 Optional: jsonrpcpp for more robust JSON-RPC (can add later)

## Risk Management

| Risk | Mitigation | Status |
|------|------------|---------|
| Pattern matching bugs | Test with Test 1.1's 4 patterns first | 🟡 Medium |
| Harmony protocol mismatch | Start with minimal, add incrementally | 🟢 Low |
| Memory leaks | Use RAII, smart pointers everywhere | 🟢 Low |
| Timeout complexity | Simple std::async approach | 🟡 Medium |

## Success Criteria

The infrastructure is complete when:
- [ ] A C++ developer can implement Test 1.2 following README.md
- [ ] Both test modes work: `./test.sh 1.2` and `../../test.sh cpp 1.2`
- [ ] No modifications needed to infrastructure for new scenarios
- [ ] Pattern matching handles all common step types
- [ ] 30-second timeout prevents hanging tests