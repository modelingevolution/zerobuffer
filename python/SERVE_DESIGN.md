# ZeroBuffer Python Serve Design

This document outlines the design and implementation strategy for the Python serve application that integrates with the Harmony testing framework.

## Overview

The Python serve application provides a JSON-RPC server that executes test steps for the ZeroBuffer cross-platform testing infrastructure. It follows the same protocol as the C# and C++ implementations, enabling full interoperability testing across all three languages.

## Architecture

### Core Components

```
zerobuffer_serve.py              # Entry point
zerobuffer_serve/
├── server.py                    # JSON-RPC server implementation
├── models.py                    # Request/Response data models
├── step_registry.py             # Step discovery and execution engine
├── test_context.py              # Test state management
├── logging/
│   ├── dual_logger.py          # Dual logging (stderr + in-memory)
│   └── log_collector.py        # In-memory log collection
└── step_definitions/
    ├── base.py                 # Base step definition class
    ├── basic_communication.py  # Basic communication test steps
    ├── benchmarks.py           # Performance benchmark steps
    └── ...                     # Other step implementations
```

### Design Principles

1. **Async-First**: Built on asyncio for non-blocking I/O operations
2. **Type-Safe**: Extensive use of type hints and runtime validation
3. **Discoverable**: Steps are automatically discovered using decorators
4. **Compatible**: Matches C# JSON-RPC contract exactly
5. **Testable**: Dependency injection and clear separation of concerns

## JSON-RPC Protocol

The server implements the following JSON-RPC methods over stdin/stdout:

### 1. Health Check
```json
// Request
{
  "jsonrpc": "2.0",
  "method": "health",
  "params": {"hostPid": 12345, "featureId": 67},
  "id": "1"
}

// Response
{
  "jsonrpc": "2.0",
  "result": true,
  "id": "1"
}
```

### 2. Initialize
```json
// Request
{
  "jsonrpc": "2.0",
  "method": "initialize",
  "params": {
    "hostPid": 12345,
    "featureId": 67,
    "role": "reader",
    "platform": "python",
    "scenario": "Test 1.1 - Simple Write-Read Cycle",
    "testRunId": "run-12345"
  },
  "id": "2"
}

// Response
{
  "jsonrpc": "2.0",
  "result": true,
  "id": "2"
}
```

### 3. Discover Steps
```json
// Request
{
  "jsonrpc": "2.0",
  "method": "discover",
  "params": {},
  "id": "3"
}

// Response
{
  "jsonrpc": "2.0",
  "result": {
    "steps": [
      {"type": "given", "pattern": "the test environment is initialized"},
      {"type": "when", "pattern": "the '([^']+)' process connects to buffer '([^']+)'"},
      // ... more steps
    ]
  },
  "id": "3"
}
```

### 4. Execute Step
```json
// Request
{
  "jsonrpc": "2.0",
  "method": "executeStep",
  "params": {
    "process": "reader",
    "stepType": "given",
    "step": "creates buffer 'test' with metadata size '1024'",
    "originalStep": "Given the 'reader' process creates buffer 'test'",
    "parameters": {"buffer_name": "test", "metadata_size": "1024"},
    "isBroadcast": false
  },
  "id": "4"
}

// Response
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "error": null,
    "data": {},
    "logs": [
      {"level": "INFO", "message": "Created buffer 'test'"}
    ]
  },
  "id": "4"
}
```

### 5. Cleanup & Shutdown
```json
// Cleanup
{
  "jsonrpc": "2.0",
  "method": "cleanup",
  "params": {},
  "id": "5"
}

// Shutdown (no response expected)
{
  "jsonrpc": "2.0",
  "method": "shutdown",
  "params": {}
}
```

## Implementation Strategy

### Step Definition Pattern

Steps are defined using decorators that mimic SpecFlow/Gherkin syntax. 

**CRITICAL DESIGN PRINCIPLE**: Step definitions should NOT filter by process role. Harmony routes commands intelligently to the correct process, so each step definition should simply execute what it's asked to do.

```python
from zerobuffer_serve.step_definitions import given, when, then

class BasicCommunicationSteps:
    def __init__(self, test_context: TestContext, logger: Logger):
        self._context = test_context
        self._logger = logger
        self._readers = {}
        self._writers = {}
    
    @given(r"the '([^']+)' process creates buffer '([^']+)' with metadata size '(\d+)' and payload size '(\d+)'")
    async def create_buffer(self, process: str, buffer_name: str, metadata_size: str, payload_size: str):
        """Create a new ZeroBuffer with specified configuration"""
        # NO ROLE FILTERING - Harmony routes this to the right process
        config = BufferConfig(
            metadata_size=int(metadata_size),
            payload_size=int(payload_size)
        )
        
        reader = Reader(buffer_name, config)
        self._readers[buffer_name] = reader
        
        self._logger.info(f"Created buffer '{buffer_name}' with metadata_size={metadata_size}, payload_size={payload_size}")
        
    @when(r"the '([^']+)' process connects to buffer '([^']+)'")
    async def connect_to_buffer(self, process: str, buffer_name: str):
        """Connect a writer to an existing buffer"""
        # NO ROLE FILTERING - Just execute the step
        writer = Writer(buffer_name)
        self._writers[buffer_name] = writer
        
        self._logger.info(f"Connected to buffer '{buffer_name}'")
```

### Async JSON-RPC Server

The server uses `python-lsp-jsonrpc` for protocol handling with asyncio integration:

```python
import asyncio
from python_lsp_jsonrpc.streams import JsonRpcStreamReader, JsonRpcStreamWriter

class ZeroBufferServe:
    def __init__(self, step_executor: StepExecutor, test_context: TestContext, logger: Logger):
        self._step_executor = step_executor
        self._test_context = test_context
        self._logger = logger
        
    async def run(self):
        """Run the JSON-RPC server on stdin/stdout"""
        reader = JsonRpcStreamReader(sys.stdin.buffer)
        writer = JsonRpcStreamWriter(sys.stdout.buffer)
        
        # Register handlers
        dispatcher = {
            "health": self._health,
            "initialize": self._initialize,
            "discover": self._discover,
            "executeStep": self._execute_step,
            "cleanup": self._cleanup,
            "shutdown": self._shutdown
        }
        
        # Start processing requests
        await self._process_requests(reader, writer, dispatcher)
```

### Logging Strategy

Dual logging approach for debugging and test result collection:

```python
class DualLogger:
    """Logger that writes to stderr and collects in memory"""
    
    def __init__(self):
        self._stderr_logger = logging.getLogger("zerobuffer.serve")
        self._log_collector = LogCollector()
        
    def log(self, level: str, message: str):
        # Log to stderr for debugging
        self._stderr_logger.log(getattr(logging, level), message)
        
        # Collect for JSON-RPC response
        self._log_collector.add(LogEntry(level=level, message=message))
        
    def get_collected_logs(self) -> List[LogEntry]:
        """Get and clear collected logs"""
        return self._log_collector.get_and_clear()
```

## Dependencies

### Core Dependencies
- `python-lsp-jsonrpc>=1.1.0` - JSON-RPC protocol implementation
- `pydantic>=2.0` - Data validation and serialization

### Development Dependencies
- `pytest>=7.0` - Testing framework
- `pytest-asyncio>=0.21` - Async test support
- `pytest-mock>=3.11` - Mocking support
- `black>=23.0` - Code formatting
- `mypy>=1.0` - Static type checking
- `ruff>=0.1.0` - Fast Python linting

## Error Handling

All step executions are wrapped in exception handlers that convert Python exceptions to JSON-RPC error responses:

```python
async def execute_step(self, request: StepRequest) -> StepResponse:
    try:
        # Execute the step
        await self._step_registry.execute(request)
        
        # Collect logs
        logs = self._logger.get_collected_logs()
        
        return StepResponse(success=True, logs=logs)
        
    except Exception as e:
        self._logger.error(f"Step execution failed: {e}")
        
        return StepResponse(
            success=False,
            error=str(e),
            logs=self._logger.get_collected_logs()
        )
```

## Testing

The serve application includes comprehensive tests:

1. **Unit Tests**: Test individual components in isolation
2. **Integration Tests**: Test JSON-RPC communication
3. **Contract Tests**: Ensure compatibility with C# implementation
4. **Step Tests**: Verify step execution logic

Example test:

```python
@pytest.mark.asyncio
async def test_create_buffer_step():
    # Arrange
    test_context = TestContext()
    logger = DualLogger()
    steps = BasicCommunicationSteps(test_context, logger)
    
    # Act
    await steps.create_buffer("reader", "test-buffer", "1024", "10240")
    
    # Assert
    assert "test-buffer" in steps._readers
    logs = logger.get_collected_logs()
    assert any("Created buffer 'test-buffer'" in log.message for log in logs)
```

## Usage

To run the serve application:

```bash
# Direct execution
python zerobuffer_serve.py

# Or via Harmony
# Harmony will start the process and communicate via JSON-RPC
```