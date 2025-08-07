"""
Example showing how Pydantic enhances the developer experience in ZeroBuffer tests.

Pydantic provides:
1. Automatic validation with clear error messages
2. Environment variable support with ZEROBUFFER_TEST_ prefix
3. Type safety and IDE autocomplete
4. Automatic documentation generation
5. Settings inheritance and composition
"""

from typing import Optional
import os
from pydantic import ValidationError

from .config import TestConfig, ConfigManager, get_config, LogLevel


def example_1_automatic_validation() -> None:
    """
    Example 1: Automatic validation with clear error messages.
    
    Pydantic validates all configuration values and provides
    helpful error messages when validation fails.
    """
    print("\n=== Example 1: Automatic Validation ===")
    
    # Try to create invalid configuration
    try:
        config = TestConfig(
            buffer_timeout_ms=100000,  # Exceeds max of 60000
            log_level=LogLevel.DEBUG,  # Valid, but other fields will fail
            cpu_warning_percent=150    # Exceeds 100%
        )
    except ValidationError as e:
        print("Validation errors (user-friendly):")
        for error in e.errors():
            field = error['loc'][0]
            msg = error['msg']
            print(f"  - {field}: {msg}")
            
    # Valid configuration with IDE autocomplete
    config = TestConfig(
        buffer_timeout_ms=5000,     # IDE shows: int between 100-60000
        log_level=LogLevel.DEBUG,   # IDE shows: enum of valid levels
        performance_tracking=True   # IDE shows: bool type
    )
    print(f"\nValid config created: timeout={config.buffer_timeout_ms}ms")


def example_2_environment_variables() -> None:
    """
    Example 2: Automatic environment variable support.
    
    All settings can be overridden via environment variables
    with the ZEROBUFFER_TEST_ prefix.
    """
    print("\n=== Example 2: Environment Variable Support ===")
    
    # Set environment variables
    os.environ['ZEROBUFFER_TEST_BUFFER_TIMEOUT_MS'] = '10000'
    os.environ['ZEROBUFFER_TEST_LOG_LEVEL'] = 'DEBUG'
    os.environ['ZEROBUFFER_TEST_PERFORMANCE_TRACKING'] = 'false'
    
    # Reload configuration - automatically picks up env vars
    ConfigManager.reload()
    config = get_config()
    
    print(f"Config from environment:")
    print(f"  buffer_timeout_ms: {config.buffer_timeout_ms}")
    print(f"  log_level: {config.log_level}")
    print(f"  performance_tracking: {config.performance_tracking}")
    
    # Clean up
    del os.environ['ZEROBUFFER_TEST_BUFFER_TIMEOUT_MS']
    del os.environ['ZEROBUFFER_TEST_LOG_LEVEL']
    del os.environ['ZEROBUFFER_TEST_PERFORMANCE_TRACKING']


def example_3_type_safety_and_ide_support() -> None:
    """
    Example 3: Type safety with IDE autocomplete.
    
    Pydantic models provide full type hints that IDEs
    can use for autocomplete and type checking.
    """
    print("\n=== Example 3: Type Safety & IDE Support ===")
    
    config = get_config()
    
    # IDE knows these types and provides autocomplete:
    timeout: int = config.buffer_timeout_ms        # IDE shows: int
    log_level: str = config.log_level.value       # IDE shows: LogLevel enum
    tracking: bool = config.performance_tracking   # IDE shows: bool
    
    # Type checking prevents errors:
    # config.buffer_timeout_ms = "not a number"  # IDE error: Expected int
    # config.log_level = "INVALID"               # IDE error: Not in LogLevel enum
    
    print(f"Type-safe access:")
    print(f"  Timeout (int): {timeout}")
    print(f"  Log level (enum): {log_level}")
    print(f"  Tracking (bool): {tracking}")


def example_4_config_documentation() -> None:
    """
    Example 4: Automatic documentation generation.
    
    Pydantic can generate JSON schemas that document
    all configuration options.
    """
    print("\n=== Example 4: Auto-generated Documentation ===")
    
    # Generate JSON schema for documentation
    schema = TestConfig.schema()
    
    print("Configuration properties:")
    for prop_name, prop_info in schema['properties'].items():
        description = prop_info.get('description', 'No description')
        prop_type = prop_info.get('type', 'unknown')
        
        # Show constraints if any
        constraints = []
        if 'minimum' in prop_info:
            constraints.append(f"min={prop_info['minimum']}")
        if 'maximum' in prop_info:
            constraints.append(f"max={prop_info['maximum']}")
        if 'enum' in prop_info:
            constraints.append(f"values={prop_info['enum']}")
            
        constraint_str = f" ({', '.join(constraints)})" if constraints else ""
        
        print(f"  {prop_name} ({prop_type}{constraint_str})")
        print(f"    {description}")


def example_5_config_override_patterns() -> None:
    """
    Example 5: Configuration override patterns.
    
    Shows different ways to override configuration for
    specific test scenarios.
    """
    print("\n=== Example 5: Configuration Override Patterns ===")
    
    # Pattern 1: Override for a specific test
    test_config = ConfigManager.override_test_config(
        buffer_timeout_ms=1000,  # Faster timeout for unit tests
        performance_tracking=False,  # Disable for speed
        debug_mode=True  # Enable debug output
    )
    print(f"Test override: timeout={test_config.buffer_timeout_ms}ms")
    
    # Pattern 2: Load from test-specific file
    # Would load from .env.test.integration if it existed
    # test_config = ConfigManager.from_file(Path(".env.test.integration"))
    
    # Pattern 3: Context-specific configuration
    if os.getenv('CI'):
        # In CI environment
        ci_config = ConfigManager.override_test_config(
            parallel_tests=True,
            resource_monitoring=True,
            cleanup_on_failure=True
        )
        print("CI configuration applied")
    
    # Pattern 4: Export current config as env vars
    env_dict = test_config.to_env_dict()
    print(f"\nExportable env vars (first 3):")
    for key, value in list(env_dict.items())[:3]:
        print(f"  export {key}={value}")


def example_6_validation_in_step_definitions() -> None:
    """
    Example 6: Using Pydantic for step parameter validation.
    
    Shows how to use Pydantic models to validate step inputs.
    """
    print("\n=== Example 6: Step Parameter Validation ===")
    
    from pydantic import BaseModel, Field, field_validator
    
    class FrameWriteRequest(BaseModel):
        """Validated frame write parameters."""
        size: int = Field(ge=1, le=1048576, description="Frame size in bytes")
        sequence: int = Field(ge=0, description="Sequence number")
        pattern: str = Field(default="test", pattern="^[a-z]+$")
        
        @field_validator('size')
        @classmethod
        def size_must_be_aligned(cls, v: int) -> int:
            """Ensure size is 4-byte aligned for performance."""
            if v % 4 != 0:
                raise ValueError('Frame size must be 4-byte aligned')
            return v
    
    # Using in a step definition
    def write_frame_validated(request_data: dict) -> None:
        """Write frame with validated parameters."""
        try:
            # Pydantic validates and converts the input
            request = FrameWriteRequest(**request_data)
            print(f"Valid request: size={request.size}, seq={request.sequence}")
            # Proceed with frame write...
            
        except ValidationError as e:
            print(f"Invalid frame request: {e}")
            raise
    
    # Test with valid data
    write_frame_validated({"size": 1024, "sequence": 1})
    
    # Test with invalid data (not aligned)
    try:
        write_frame_validated({"size": 1023, "sequence": 1})
    except ValidationError:
        print("Caught validation error for unaligned size")


def example_7_settings_composition() -> None:
    """
    Example 7: Composing settings for different components.
    
    Shows how Pydantic enables clean settings composition
    for complex test scenarios.
    """
    print("\n=== Example 7: Settings Composition ===")
    
    from pydantic import BaseModel
    from typing import List
    
    class BufferSettings(BaseModel):
        """Settings for a single buffer."""
        name: str
        metadata_size: int = 1024
        payload_size: int = 10240
        
    class ScenarioSettings(BaseModel):
        """Settings for a test scenario."""
        scenario_name: str
        buffers: List[BufferSettings]
        timeout_seconds: int = 30
        parallel_execution: bool = False
        
    # Compose complex test configuration
    scenario = ScenarioSettings(
        scenario_name="Multi-buffer test",
        buffers=[
            BufferSettings(name="buffer1", payload_size=5000),
            BufferSettings(name="buffer2", metadata_size=2048),
            BufferSettings(name="buffer3")  # Uses defaults
        ],
        parallel_execution=True
    )
    
    print(f"Scenario: {scenario.scenario_name}")
    for buffer in scenario.buffers:
        print(f"  Buffer '{buffer.name}': {buffer.payload_size} bytes")
    
    # Export as dict for easy serialization
    scenario_dict = scenario.model_dump()
    print(f"\nSerializable: {type(scenario_dict)}")


def main() -> None:
    """Run all examples showing Pydantic developer experience benefits."""
    
    print("=" * 60)
    print("PYDANTIC DEVELOPER EXPERIENCE IN ZEROBUFFER TESTS")
    print("=" * 60)
    
    example_1_automatic_validation()
    example_2_environment_variables()
    example_3_type_safety_and_ide_support()
    example_4_config_documentation()
    example_5_config_override_patterns()
    example_6_validation_in_step_definitions()
    example_7_settings_composition()
    
    print("\n" + "=" * 60)
    print("KEY DEVELOPER EXPERIENCE BENEFITS:")
    print("=" * 60)
    print("""
1. **Validation**: Automatic validation with clear error messages
2. **Environment**: Seamless env var support (ZEROBUFFER_TEST_*)
3. **Type Safety**: Full IDE autocomplete and type checking
4. **Documentation**: Auto-generated config documentation
5. **Flexibility**: Easy override patterns for different scenarios
6. **Composition**: Clean settings composition for complex tests
7. **Serialization**: Easy conversion to/from dict, JSON, env vars

These features significantly reduce bugs, improve maintainability,
and make the codebase more accessible to new developers.
    """)


if __name__ == "__main__":
    main()