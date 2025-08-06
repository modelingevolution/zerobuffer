# Python Test Development Guide

## Prerequisites
1. Read ZeroBuffer protocol documentation (all markdown files)
2. Read harmony documentation (all markdown files)
3. Install test dependencies: `pip install -r requirements-test.txt`

## Architecture Overview

Python tests use **pytest-bdd** to execute scenarios directly from feature files, ensuring tests stay synchronized with the source of truth.

```
Feature Files (Source of Truth)
├── ZeroBuffer.Harmony.Tests/Features/*.feature
│
├── Local Testing (pytest-bdd)
│   └── Reads feature files directly
│       └── Executes Python step definitions
│
└── Harmony Testing (JSON-RPC)
    └── Receives steps via JSON-RPC
        └── Executes same Python step definitions
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
- Steps mentioning `'([^']+)' process` should accept the process parameter
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
```python
class BasicCommunicationSteps:
    def __init__(self, test_context, logger):
        self._readers: Dict[str, Reader] = {}
        self._writers: Dict[str, Writer] = {}
        self._last_frame: Optional[Frame] = None
        self._last_exception: Optional[Exception] = None
```

### Error Handling
- Expected failures: capture exception, don't throw
- Store in `_last_exception` for Then validation

```python
@when("the writer attempts to write oversized frame")
async def write_oversized_frame(self):
    try:
        oversized_data = b'x' * (self.buffer_size + 1)
        self._writer.write_frame(oversized_data)
    except FrameTooLargeException as e:
        self._last_exception = e

@then("the write should fail with frame too large error")
def verify_frame_too_large(self):
    assert isinstance(self._last_exception, FrameTooLargeException)
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
# Run existing unit tests
./venv/bin/pytest tests/test_zerobuffer.py -v

# Run scenario tests
./venv/bin/pytest tests/test_scenarios.py -v
```

## Development Process - YOUR TODO LIST

0. **READ THE PREREQUISITES** if you haven't!
1. Read scenario in feature file and understand the intent
2. Check if step definitions exist for all steps
3. Implement missing step definitions with pytest-bdd decorators
4. Run locally with pytest-bdd: `./venv/bin/pytest tests/test_features_bdd.py -k [scenario_name]`
5. Fix any issues with step matching or implementation
6. Test with Harmony if cross-platform validation needed

## Implementation Rules

1. **Use pytest-bdd decorators** - `@given`, `@when`, `@then` from pytest-bdd
2. **Match feature file exactly** - Step patterns must match Gherkin text
3. **No process filtering** - Accept process parameter but don't filter by it
4. **Real implementation** - No empty logging steps, implement actual logic
5. **Single source of truth** - Feature files drive all behavior

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

### Test Data Patterns

```python
class TestDataPatterns:
    @staticmethod
    def generate_frame_data(size: int, sequence: int) -> bytes:
        """Generate consistent test data"""
        pattern = f"Frame_{sequence:04d}_"
        return (pattern * (size // len(pattern) + 1))[:size].encode()
    
    @staticmethod
    def verify_frame_data(data: bytes, expected_sequence: int) -> bool:
        """Verify frame data matches expected pattern"""
        expected = TestDataPatterns.generate_frame_data(len(data), expected_sequence)
        return data == expected
```

## Troubleshooting

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

## Best Practices

1. **Keep step definitions focused** - One action per step
2. **Use descriptive names** - Method names should explain the action
3. **Share state carefully** - Use class attributes for cross-step data
4. **Test both paths** - Verify both local (pytest-bdd) and Harmony execution
5. **Document complex patterns** - Add comments for non-obvious regex patterns

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

## Continuous Integration

Add to CI pipeline:
```yaml
# .github/workflows/test.yml
- name: Run Python BDD Tests
  run: |
    cd python
    pip install -r requirements-test.txt
    pytest tests/test_features_bdd.py --junit-xml=test-results.xml
```

## Getting Help

1. **Protocol questions**: Read the protocol documentation in project root
2. **Harmony questions**: Read harmony documentation
3. **pytest-bdd issues**: Check [pytest-bdd documentation](https://pytest-bdd.readthedocs.io/)
4. **Step implementation**: Look at existing examples in `step_definitions/`

Remember: The feature files are the **single source of truth**. If there's a discrepancy between feature files and implementation, the feature files are correct!