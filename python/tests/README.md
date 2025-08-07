# Python Test Development Guide

## Prerequisites
1. Read ZeroBuffer protocol documentation (all markdown files)
2. Read harmony documentation (all markdown files)
3. Install test dependencies: `pip install -r requirements-test.txt`

## Production Engineering Requirements

### Code Quality Standards
- **Type Hints**: All functions MUST have complete type annotations
- **Docstrings**: All public methods require docstrings (Google/NumPy style)
- **Error Handling**: Explicit exception types, no bare `except:` clauses
- **Async/Await**: Proper async context management, no blocking I/O in async functions
- **Resource Management**: Use context managers for all resources (buffers, semaphores)
- **Logging**: Structured logging with appropriate levels (no print statements)

### Architecture Principles

#### 1. **Dependency Injection**
```python
class BasicCommunicationSteps:
    def __init__(
        self, 
        test_context: TestContext,
        logger: Logger,
        buffer_factory: Optional[BufferFactory] = None
    ):
        self._test_context = test_context
        self._logger = logger
        self._buffer_factory = buffer_factory or BufferFactory()
```

#### 2. **Interface Segregation**
```python
from abc import ABC, abstractmethod
from typing import Protocol

class BufferReader(Protocol):
    """Interface for buffer reading operations"""
    def read_frame(self, timeout_ms: int) -> Frame: ...
    def get_metadata(self) -> bytes: ...

class BufferWriter(Protocol):
    """Interface for buffer writing operations"""
    def write_frame(self, data: bytes) -> None: ...
    def set_metadata(self, data: bytes) -> None: ...
```

#### 3. **Domain-Driven Design**
- Step definitions are **application services**
- ZeroBuffer Reader/Writer are **domain entities**
- TestContext is an **aggregate root**
- BufferConfig is a **value object**

### Testing Infrastructure Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Test Orchestration Layer              │
│  ┌──────────────┐                    ┌──────────────┐  │
│  │ pytest-bdd   │                    │   Harmony    │  │
│  │   Runner     │                    │  JSON-RPC    │  │
│  └──────┬───────┘                    └──────┬───────┘  │
│         │                                    │          │
│         └──────────────┬─────────────────────┘          │
│                        ▼                                │
│              ┌──────────────────┐                       │
│              │  Step Registry   │                       │
│              │  (Singleton)     │                       │
│              └────────┬─────────┘                       │
│                       ▼                                 │
│         ┌──────────────────────────┐                   │
│         │   Step Definitions       │                   │
│         │  (Service Layer)         │                   │
│         └──────────┬───────────────┘                   │
│                    ▼                                    │
│      ┌─────────────────────────────────┐              │
│      │     Domain Layer                │              │
│      │  ┌─────────┐  ┌─────────┐     │              │
│      │  │ Reader  │  │ Writer  │     │              │
│      │  └─────────┘  └─────────┘     │              │
│      └─────────────────────────────────┘              │
└─────────────────────────────────────────────────────────┘
```

## File Locations
- **Feature Files**: `../../ZeroBuffer.Harmony.Tests/Features/` (source of truth)
- **Step Definitions**: `zerobuffer_serve/step_definitions/` (shared by both modes)
- **Test Runners**: `tests/test_features_bdd.py` (pytest-bdd integration)
- **ZeroBuffer Library**: `zerobuffer/` (core Python implementation)

## Step Definition Implementation

### Using pytest-bdd Decorators

Step definitions use pytest-bdd decorators that work for both local and Harmony testing:

```python
from pytest_bdd import given, when, then, parsers
from zerobuffer import Reader, Writer, BufferConfig

class BasicCommunicationSteps:
    
    @given(parsers.re(r"the '(?P<process>[^']+)' process creates buffer '(?P<buffer>[^']+)' with metadata size '(?P<meta>\d+)' and payload size '(?P<payload>\d+)'"))
    async def create_buffer(self, process: str, buffer: str, meta: str, payload: str):
        """Create a new ZeroBuffer with specified configuration"""
        config = BufferConfig(
            metadata_size=int(meta),
            payload_size=int(payload)
        )
        
        reader = Reader(buffer, config)
        self._readers[buffer] = reader
        
        self.logger.info(f"Created buffer '{buffer}'")
```

### Process Parameters
- Steps with `the '([^']+)' process` MUST accept but ignore the process parameter
- **'And' steps MUST NOT have process parameters** (inherit context from previous step)
- Exception: 'And' steps with explicit process switch
- **DO NOT filter by process** - Harmony routes steps to the correct process
- Process parameter is for context only, not for conditional logic

```python
# CORRECT - Accept process but don't filter
@given(parsers.re(r"the '(?P<process>[^']+)' process creates buffer"))
async def create_buffer(self, process: str, buffer: str):
    # Just execute - Harmony already routed this correctly
    reader = Reader(buffer, config)
    
# WRONG - Don't filter by process
@given(parsers.re(r"the '(?P<process>[^']+)' process creates buffer"))
async def create_buffer(self, process: str, buffer: str):
    if self.context.role != process:  # WRONG! Don't do this!
        return
```

### State Management

**Production Requirements:**
- Use immutable data structures where possible
- Thread-safe state management for concurrent tests
- Clear separation between test state and system state

```python
from dataclasses import dataclass, field
from typing import Dict, Optional, Any
from threading import Lock
import contextlib

@dataclass
class TestState:
    """Immutable test state snapshot"""
    readers: Dict[str, Reader] = field(default_factory=dict)
    writers: Dict[str, Writer] = field(default_factory=dict)
    last_frame: Optional[Frame] = None
    last_exception: Optional[Exception] = None
    properties: Dict[str, Any] = field(default_factory=dict)

class BasicCommunicationSteps:
    def __init__(
        self, 
        test_context: TestContext,
        logger: Logger
    ) -> None:
        self._test_context = test_context
        self._logger = logger
        self._state = TestState()
        self._state_lock = Lock()
        
    @contextlib.contextmanager
    def _atomic_state_update(self):
        """Ensure thread-safe state updates"""
        with self._state_lock:
            # Create mutable copy
            new_state = dataclasses.replace(self._state)
            yield new_state
            # Commit changes
            self._state = new_state
```

### Error Handling

**Production Requirements:**
- Custom exception hierarchy
- Proper exception chaining
- Detailed error context
- Recovery strategies

```python
from typing import Type, Optional, Callable, TypeVar
from functools import wraps
import traceback

# Custom exception hierarchy
class ZeroBufferTestException(Exception):
    """Base exception for test failures"""
    pass

class StepExecutionException(ZeroBufferTestException):
    """Step execution failed"""
    def __init__(self, step: str, cause: Exception):
        self.step = step
        self.cause = cause
        super().__init__(f"Step '{step}' failed: {cause}")

class AssertionException(ZeroBufferTestException):
    """Test assertion failed"""
    pass

# Exception handling decorator
T = TypeVar('T')

def capture_expected_exception(*exception_types: Type[Exception]):
    """Decorator to capture expected exceptions for validation"""
    def decorator(func: Callable[..., T]) -> Callable[..., T]:
        @wraps(func)
        async def async_wrapper(self, *args, **kwargs) -> Optional[T]:
            try:
                return await func(self, *args, **kwargs)
            except exception_types as e:
                self._logger.info(f"Expected exception captured: {type(e).__name__}")
                with self._atomic_state_update() as state:
                    state.last_exception = e
                return None
            except Exception as e:
                self._logger.error(f"Unexpected exception in {func.__name__}: {e}")
                raise StepExecutionException(func.__name__, e) from e
                
        @wraps(func)
        def sync_wrapper(self, *args, **kwargs) -> Optional[T]:
            try:
                return func(self, *args, **kwargs)
            except exception_types as e:
                self._logger.info(f"Expected exception captured: {type(e).__name__}")
                with self._atomic_state_update() as state:
                    state.last_exception = e
                return None
            except Exception as e:
                self._logger.error(f"Unexpected exception in {func.__name__}: {e}")
                raise StepExecutionException(func.__name__, e) from e
                
        return async_wrapper if asyncio.iscoroutinefunction(func) else sync_wrapper
    return decorator

# Usage example
@when("the writer attempts to write oversized frame")
@capture_expected_exception(FrameTooLargeException, BufferFullException)
async def write_oversized_frame(self) -> None:
    """
    Attempt to write a frame larger than buffer capacity.
    
    Raises:
        FrameTooLargeException: Expected when frame exceeds buffer size
        BufferFullException: Possible if buffer is already near capacity
    """
    oversized_data = b'x' * (self.buffer_size + 1)
    await self._writer.write_frame_async(oversized_data)

@then("the write should fail with frame too large error")
def verify_frame_too_large(self) -> None:
    """Verify that the expected exception was raised"""
    if not isinstance(self._state.last_exception, FrameTooLargeException):
        raise AssertionException(
            f"Expected FrameTooLargeException, got {type(self._state.last_exception).__name__}"
        )
```

## Running Tests

### Local Testing with test.sh script

```bash
# Run all BDD tests
./test.sh

# Run specific test by number
./test.sh 1.1

# Run tests matching a pattern
./test.sh BasicCommunication

# Run with verbose output
./test.sh -v 1.1
```

### Harmony Cross-Platform Testing

**IMPORTANT**: To run tests through Harmony (cross-platform with C#/C++/Python):

```bash
# From the zerobuffer directory (parent of python/)
cd ..

# Run Python-Python tests (both reader and writer in Python)
./test.sh python 1.1

# Run Python-CSharp tests (Python reader, C# writer or vice versa)
./test.sh python_csharp 1.1

# Run CSharp-Python tests
./test.sh csharp_python 1.1

# The test.sh script is in the zerobuffer directory
# The format is: ./test.sh [platform_combination] [test_number]
```

**Note**: The Harmony test runner (`test.sh`) expects:
- Platform combinations like: `python`, `python_python`, `python_csharp`, `csharp_python`
- Test numbers like: `1.1`, `1.2`, `4.3`, etc.

### Traditional Python Tests

```bash
# RUN test.sh in python folder
./test.sh [number]
```

## Development Process - YOUR TODO LIST

### Pre-Development Checklist
- [ ] **READ ALL PREREQUISITES** (Protocol docs, Harmony docs)
- [ ] **Setup environment**: Virtual environment activated, dependencies installed
- [ ] **Verify test infrastructure**: `./test.sh 1.1` passes (baseline test)

### For Each New Scenario

#### 1. Analysis Phase
- [ ] **Read scenario** in feature file
- [ ] **Validate scenario logic** - Does it make sense? Ask if unclear
- [ ] **Check data exchange requirements** - If processes need shared state, STOP and discuss architecture
- [ ] **Review reference implementation** - Check C# version in `../../csharp/ZeroBuffer.Tests/StepDefinitions/`

#### 2. Design Phase
- [ ] **Identify step definitions** needed
- [ ] **Design state management** - What needs to be tracked?
- [ ] **Plan error scenarios** - Which exceptions are expected?
- [ ] **Define performance requirements** - Any steps that are time-critical?

#### 3. Implementation Phase
- [ ] **Create/update step definition class** with proper structure:
  ```python
  class ScenarioNameSteps:
      def __init__(self, test_context: TestContext, logger: Logger):
          # Dependency injection
      
      @given(parsers.re(r"..."))
      @performance_critical(threshold_ms=100)
      async def step_name(self, ...) -> None:
          """Docstring with Args, Returns, Raises"""
          # Real implementation - NO empty logging!
  ```
- [ ] **Add type hints** to ALL parameters and returns
- [ ] **Implement error handling** with custom exceptions
- [ ] **Add performance monitoring** for critical operations
- [ ] **Write comprehensive docstrings**

#### 4. Testing Phase
- [ ] **Unit test the step** in isolation (if complex logic)
- [ ] **Run single test locally**: `./test.sh [test-number]`
  - [ ] Verify no memory leaks
  - [ ] Check performance metrics
  - [ ] Review logs for warnings
- [ ] **Fix any failures** - Read protocol docs, check implementation
- [ ] **Run with resource monitoring** enabled

#### 5. Integration Phase
- [ ] **ONLY if local test GREEN**: Run with Harmony
  - [ ] `cd ..` (to parent directory)
  - [ ] `./test.sh python [test-number]`
- [ ] **Test cross-platform combinations**:
  - [ ] `./test.sh python_csharp [test-number]`
  - [ ] `./test.sh csharp_python [test-number]`
- [ ] **Verify no resource leaks** in cross-platform scenarios

#### 6. Code Review Checklist
- [ ] **No empty logging steps** - Every step has real implementation
- [ ] **Pattern matches exactly** - Regex matches feature file text
- [ ] **No unused steps** - Delete any orphaned definitions
- [ ] **No process filtering** - Steps work for any process
- [ ] **Type safety** - Full type annotations
- [ ] **Error handling** - All exceptions caught and handled
- [ ] **Performance** - Meets timing requirements
- [ ] **Documentation** - Docstrings and comments where needed

### Post-Implementation
- [ ] **Update test metrics** if new patterns discovered
- [ ] **Document any gotchas** in this README
- [ ] **Share learnings** with team if complex issue solved

## Implementation Rules

1. **Every step must have real implementation** - no empty logging steps
2. **Match feature file exactly** - regex must match feature file text
3. **No unused steps** - delete if not in feature file
4. **No process-specific logic** - treat all processes the same, the process might only refer to specific zero-buffer client (reader/writer)
5. **Use pytest-bdd decorators** - `@given`, `@when`, `@then` from pytest-bdd
6. **Single source of truth** - Feature files drive all behavior

## Common Patterns

### Pattern Matching with pytest-bdd

```python
from pytest_bdd import given, when, then, parsers

# Simple string matching
@given("the test environment is initialized")
def init_env(self):
    pass

# Regex with named groups
@given(parsers.re(r"buffer '(?P<name>[^']+)' exists"))
def buffer_exists(self, name: str):
    pass

# Parse with type conversion
@given(parsers.parse("buffer size is {size:d} bytes"))
def set_buffer_size(self, size: int):  # size is already int!
    pass
```

### Async Step Definitions

```python
@when("the writer writes a frame")
async def write_frame(self):
    await self._writer.write_frame_async(data)

# For sync test runners, wrap async calls
def run_async(coro):
    import asyncio
    loop = asyncio.get_event_loop()
    return loop.run_until_complete(coro)
```

### Buffer Naming Service
Use `BufferNamingService` to ensure unique buffer names across processes:
```python
from zerobuffer_serve.services import BufferNamingService

# Creates unique buffer names for test isolation
naming_service = BufferNamingService(logger)
actual_name = naming_service.get_buffer_name("test-buffer")
reader = Reader(actual_name, config)
```

### Test Data Patterns
Use `TestDataPatterns` for consistent data generation and assertions across processes:
```python
from zerobuffer_serve.test_data_patterns import TestDataPatterns

# Writer process: Generate frame data
data = TestDataPatterns.generate_frame_data(size=1024, sequence=1)
writer.write_frame(data)

# Reader process: Generate expected data for assertion
expected_data = TestDataPatterns.generate_frame_data(len(frame_data), frame_sequence)
assert frame_data == expected_data
```

## Troubleshooting

### IMPORTANT RULES:
1. **test.sh scripts may fail due to wrong implementation of those scripts**. If they do not work, fix them. With them you should be able to run single tests easily. If this is not possible do not look for workaround! Fix the script or ask for help.
   - Local tests: `python/test.sh`
   - Harmony cross-process tests: `../test.sh` (from parent directory)
2. **If problems occur with test implementation**, you can run the tests in C# to see how they are implemented. The C# test implementations in `../../csharp/ZeroBuffer.Tests/StepDefinitions/` provide a reference for the expected behavior and can help clarify any ambiguities in the feature files.
3. **Do not work on workarounds the Development Process**. If you cannot proceed with next points from the Development Process, ask for help and stop working.

## Common Troubleshooting

### Step Not Found
```
No step definition found for: Given the 'reader' process creates buffer
```
**Solution**: Check that:
1. Step definition exists with matching pattern
2. Pattern uses correct pytest-bdd syntax
3. Step class is registered in test runner

### Import Errors
```
ImportError: cannot import name 'Reader' from 'zerobuffer'
```
**Solution**: Ensure virtual environment is activated and dependencies installed:
```bash
source venv/bin/activate
pip install -e .
pip install -r requirements-test.txt
```

### Async Execution Issues
```
RuntimeError: This event loop is already running
```
**Solution**: Use `pytest-asyncio` or wrap async calls properly:
```python
@pytest.mark.asyncio
async def test_async_operation():
    await some_async_function()
```

### Feature File Not Found
```
FileNotFoundError: Feature file not found
```
**Solution**: Check `pytest.ini` configuration:
```ini
[pytest]
bdd_features_base_dir = ../../ZeroBuffer.Harmony.Tests/Features
```

## Key Differences from C# Tests

| Aspect | C# (SpecFlow) | Python (pytest-bdd) |
|--------|---------------|---------------------|
| **Decorators** | `[Given]`, `[When]`, `[Then]` | `@given`, `@when`, `@then` |
| **Pattern Syntax** | `@"regex"` | `parsers.re(r"regex")` |
| **Async Support** | `async Task` | `async def` with asyncio |
| **Test Discovery** | Compile-time | Runtime via pytest |
| **Feature Files** | Auto-copied to project | Read from source location |

## Performance and Monitoring

### Performance Requirements

```python
import time
import functools
from contextlib import contextmanager
from typing import Dict, List
import statistics

class PerformanceMonitor:
    """Track performance metrics for test steps"""
    
    def __init__(self, logger: Logger):
        self._logger = logger
        self._metrics: Dict[str, List[float]] = {}
        
    @contextmanager
    def measure(self, operation: str):
        """Context manager to measure operation duration"""
        start = time.perf_counter()
        try:
            yield
        finally:
            duration = time.perf_counter() - start
            self._metrics.setdefault(operation, []).append(duration)
            
            if duration > 1.0:  # Log slow operations
                self._logger.warning(
                    f"Slow operation: {operation} took {duration:.3f}s"
                )
    
    def get_statistics(self, operation: str) -> Dict[str, float]:
        """Get performance statistics for an operation"""
        timings = self._metrics.get(operation, [])
        if not timings:
            return {}
            
        return {
            "min": min(timings),
            "max": max(timings),
            "mean": statistics.mean(timings),
            "median": statistics.median(timings),
            "p95": statistics.quantiles(timings, n=20)[18] if len(timings) > 20 else max(timings),
            "count": len(timings)
        }

def performance_critical(threshold_ms: float = 100):
    """Decorator for performance-critical operations"""
    def decorator(func):
        @functools.wraps(func)
        async def async_wrapper(self, *args, **kwargs):
            start = time.perf_counter()
            try:
                result = await func(self, *args, **kwargs)
                duration_ms = (time.perf_counter() - start) * 1000
                
                if duration_ms > threshold_ms:
                    self._logger.warning(
                        f"{func.__name__} exceeded threshold: "
                        f"{duration_ms:.2f}ms > {threshold_ms}ms"
                    )
                    
                return result
            except Exception as e:
                duration_ms = (time.perf_counter() - start) * 1000
                self._logger.error(
                    f"{func.__name__} failed after {duration_ms:.2f}ms: {e}"
                )
                raise
                
        @functools.wraps(func)
        def sync_wrapper(self, *args, **kwargs):
            start = time.perf_counter()
            try:
                result = func(self, *args, **kwargs)
                duration_ms = (time.perf_counter() - start) * 1000
                
                if duration_ms > threshold_ms:
                    self._logger.warning(
                        f"{func.__name__} exceeded threshold: "
                        f"{duration_ms:.2f}ms > {threshold_ms}ms"
                    )
                    
                return result
            except Exception as e:
                duration_ms = (time.perf_counter() - start) * 1000
                self._logger.error(
                    f"{func.__name__} failed after {duration_ms:.2f}ms: {e}"
                )
                raise
                
        return async_wrapper if asyncio.iscoroutinefunction(func) else sync_wrapper
    return decorator
```

### Resource Monitoring

```python
import psutil
import resource
from dataclasses import dataclass

@dataclass
class ResourceSnapshot:
    """System resource snapshot"""
    memory_mb: float
    cpu_percent: float
    open_files: int
    threads: int
    
class ResourceMonitor:
    """Monitor system resource usage during tests"""
    
    def __init__(self, logger: Logger, warn_memory_mb: float = 500):
        self._logger = logger
        self._warn_memory_mb = warn_memory_mb
        self._process = psutil.Process()
        self._initial_snapshot = self.capture()
        
    def capture(self) -> ResourceSnapshot:
        """Capture current resource usage"""
        return ResourceSnapshot(
            memory_mb=self._process.memory_info().rss / 1024 / 1024,
            cpu_percent=self._process.cpu_percent(),
            open_files=len(self._process.open_files()),
            threads=self._process.num_threads()
        )
    
    def check_resources(self) -> None:
        """Check for resource leaks or excessive usage"""
        current = self.capture()
        
        # Check memory usage
        if current.memory_mb > self._warn_memory_mb:
            self._logger.warning(
                f"High memory usage: {current.memory_mb:.1f}MB"
            )
            
        # Check for file descriptor leaks
        if current.open_files > self._initial_snapshot.open_files + 10:
            self._logger.warning(
                f"Possible file descriptor leak: "
                f"{current.open_files} open files"
            )
```

## Production Best Practices

### 1. **Code Organization**
- One class per feature file
- Separate concerns: steps, state, utilities
- Use composition over inheritance
- Follow SOLID principles

### 2. **Type Safety**
```python
from typing import NewType, Union, Literal

BufferName = NewType('BufferName', str)
ProcessName = NewType('ProcessName', str)
FrameData = Union[bytes, bytearray, memoryview]
StepType = Literal['given', 'when', 'then']
```

### 3. **Async Best Practices**
```python
import asyncio
from asyncio import Queue, Event, Lock

class AsyncStepManager:
    """Manage async step execution with proper cleanup"""
    
    def __init__(self):
        self._tasks: List[asyncio.Task] = []
        self._cleanup_event = Event()
        
    async def run_step_with_timeout(
        self, 
        coro: Coroutine,
        timeout: float = 30.0
    ) -> Any:
        """Run step with timeout and cleanup"""
        task = asyncio.create_task(coro)
        self._tasks.append(task)
        
        try:
            return await asyncio.wait_for(task, timeout)
        except asyncio.TimeoutError:
            task.cancel()
            raise StepTimeoutException(f"Step timed out after {timeout}s")
        finally:
            self._tasks.remove(task)
    
    async def cleanup(self) -> None:
        """Cancel all pending tasks"""
        for task in self._tasks:
            task.cancel()
        
        await asyncio.gather(*self._tasks, return_exceptions=True)
        self._tasks.clear()
```

### 4. **Observability**
```python
import structlog
from opentelemetry import trace, metrics

# Structured logging
logger = structlog.get_logger()

# Distributed tracing
tracer = trace.get_tracer(__name__)

# Metrics
meter = metrics.get_meter(__name__)
step_counter = meter.create_counter(
    "test_steps_executed",
    description="Number of test steps executed"
)

@when("some step")
def some_step(self):
    with tracer.start_as_current_span("execute_step") as span:
        span.set_attribute("step.type", "when")
        span.set_attribute("step.name", "some step")
        
        step_counter.add(1, {"step_type": "when"})
        
        logger.info(
            "Executing step",
            step_type="when",
            step_name="some step",
            trace_id=span.get_span_context().trace_id
        )
```

### 5. **Configuration Management**
```python
from pydantic import BaseSettings, Field
from typing import Optional

class TestConfig(BaseSettings):
    """Test configuration with validation"""
    
    buffer_timeout_ms: int = Field(5000, ge=100, le=60000)
    max_frame_size: int = Field(1048576, ge=1, le=104857600)
    log_level: str = Field("INFO", regex="^(DEBUG|INFO|WARNING|ERROR)$")
    performance_tracking: bool = True
    resource_monitoring: bool = True
    
    class Config:
        env_prefix = "ZEROBUFFER_TEST_"
        env_file = ".env.test"
```

### 6. **Testing Patterns**
- **Arrange-Act-Assert** in every step
- **Given-When-Then** maps to setup-execute-verify
- **Test isolation** via TestContext reset
- **Deterministic tests** with seeded random
- **Property-based testing** for edge cases

## Advanced Features

### Scenario Outlines
```python
@given(parsers.parse("buffer size is {size:d}"))
def set_size(size: int):
    # Automatically handles Examples table from feature file
    pass
```

### Table Data
```python
@given("the following buffers exist:")
def create_buffers(datatable):
    for row in datatable:
        name = row['name']
        size = int(row['size'])
        create_buffer(name, size)
```

### Fixtures and Context
```python
@pytest.fixture
def test_context():
    """Shared context for all steps"""
    context = TestContext()
    yield context
    context.cleanup()

@pytest.fixture
def buffer_factory(test_context):
    """Factory for creating test buffers"""
    def factory(name: str, size: int = 1024):
        return Reader(name, BufferConfig(payload_size=size))
    return factory
```


## Getting Help

1. **Protocol questions**: Read the protocol documentation in project root
2. **Harmony questions**: Read harmony documentation
3. **pytest-bdd issues**: Check [pytest-bdd documentation](https://pytest-bdd.readthedocs.io/)
4. **Step implementation**: Look at existing examples in `step_definitions/`

Remember: The feature files are the **single source of truth**. If there's a discrepancy between feature files and implementation, the feature files are correct!