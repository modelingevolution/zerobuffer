"""
Configuration management for ZeroBuffer tests.

Provides validated configuration with environment variable support
and type safety using Pydantic.
"""

import os
from enum import Enum
from pathlib import Path
from typing import Optional, Dict, Any, List, Union
from pydantic import Field, field_validator, model_validator
from pydantic_settings import BaseSettings


class LogLevel(str, Enum):
    """Supported log levels."""
    DEBUG = "DEBUG"
    INFO = "INFO"
    WARNING = "WARNING"
    ERROR = "ERROR"
    CRITICAL = "CRITICAL"


class ProcessingMode(str, Enum):
    """Test processing modes."""
    SYNC = "sync"
    ASYNC = "async"
    THREADED = "threaded"


class Platform(str, Enum):
    """Supported platforms."""
    PYTHON = "python"
    CSHARP = "csharp"
    CPP = "cpp"


class TestConfig(BaseSettings):
    """
    Test configuration with validation.
    
    All configuration can be overridden via environment variables
    with the prefix ZEROBUFFER_TEST_.
    
    Example:
        ZEROBUFFER_TEST_BUFFER_TIMEOUT_MS=10000
        ZEROBUFFER_TEST_LOG_LEVEL=DEBUG
    """
    
    # Timeouts and limits
    buffer_timeout_ms: int = Field(
        default=5000,
        ge=100,
        le=60000,
        description="Buffer operation timeout in milliseconds"
    )
    
    frame_timeout_ms: int = Field(
        default=1000,
        ge=10,
        le=30000,
        description="Frame read/write timeout in milliseconds"
    )
    
    max_frame_size: int = Field(
        default=1048576,  # 1MB
        ge=1,
        le=104857600,  # 100MB
        description="Maximum frame size in bytes"
    )
    
    max_metadata_size: int = Field(
        default=4096,
        ge=1,
        le=1048576,
        description="Maximum metadata size in bytes"
    )
    
    # Logging configuration
    log_level: LogLevel = Field(
        default=LogLevel.INFO,
        description="Logging level"
    )
    
    log_format: str = Field(
        default="json",
        pattern="^(json|text)$",
        description="Log output format"
    )
    
    log_output: str = Field(
        default="stdout",
        description="Log output destination (stdout/stderr/file path)"
    )
    
    # Performance settings
    performance_tracking: bool = Field(
        default=True,
        description="Enable performance tracking"
    )
    
    performance_threshold_ms: float = Field(
        default=100.0,
        ge=1.0,
        le=10000.0,
        description="Performance warning threshold in milliseconds"
    )
    
    # Resource monitoring
    resource_monitoring: bool = Field(
        default=True,
        description="Enable resource monitoring"
    )
    
    memory_warning_mb: float = Field(
        default=500.0,
        ge=10.0,
        le=10000.0,
        description="Memory usage warning threshold in MB"
    )
    
    cpu_warning_percent: float = Field(
        default=80.0,
        ge=10.0,
        le=100.0,
        description="CPU usage warning threshold in percent"
    )
    
    # Test execution
    processing_mode: ProcessingMode = Field(
        default=ProcessingMode.ASYNC,
        description="Test processing mode"
    )
    
    parallel_tests: bool = Field(
        default=False,
        description="Run tests in parallel"
    )
    
    test_isolation: bool = Field(
        default=True,
        description="Ensure test isolation with cleanup"
    )
    
    # Buffer configuration
    default_buffer_size: int = Field(
        default=10240,
        ge=1024,
        le=10485760,
        description="Default buffer size in bytes"
    )
    
    default_metadata_size: int = Field(
        default=1024,
        ge=64,
        le=65536,
        description="Default metadata size in bytes"
    )
    
    # Platform settings
    platform: Platform = Field(
        default=Platform.PYTHON,
        description="Current platform"
    )
    
    # Retry configuration
    retry_attempts: int = Field(
        default=3,
        ge=0,
        le=10,
        description="Number of retry attempts for operations"
    )
    
    retry_delay_ms: int = Field(
        default=100,
        ge=10,
        le=5000,
        description="Delay between retry attempts in milliseconds"
    )
    
    # Debug settings
    debug_mode: bool = Field(
        default=False,
        description="Enable debug mode with verbose output"
    )
    
    trace_enabled: bool = Field(
        default=False,
        description="Enable distributed tracing"
    )
    
    # Cleanup settings
    cleanup_on_failure: bool = Field(
        default=True,
        description="Clean up resources on test failure"
    )
    
    cleanup_timeout_ms: int = Field(
        default=5000,
        ge=100,
        le=30000,
        description="Cleanup operation timeout in milliseconds"
    )
    
    class Config:
        """Pydantic configuration."""
        env_prefix = "ZEROBUFFER_TEST_"
        env_file = ".env.test"
        env_file_encoding = "utf-8"
        case_sensitive = False
        
    @field_validator('log_output')
    @classmethod
    def validate_log_output(cls, v: str) -> str:
        """Validate log output destination."""
        if v not in ('stdout', 'stderr'):
            # Must be a valid file path
            path = Path(v)
            if not path.parent.exists():
                raise ValueError(f"Log output directory does not exist: {path.parent}")
        return v
        
    @model_validator(mode='after')
    def validate_timeouts(self) -> 'TestConfig':
        """Validate timeout relationships."""
        if self.frame_timeout_ms > self.buffer_timeout_ms:
            raise ValueError(
                f"frame_timeout_ms ({self.frame_timeout_ms}) cannot be greater than "
                f"buffer_timeout_ms ({self.buffer_timeout_ms})"
            )
            
        return self
        
    def to_dict(self) -> Dict[str, Any]:
        """Convert configuration to dictionary."""
        return self.model_dump()
        
    def to_env_dict(self) -> Dict[str, str]:
        """Convert configuration to environment variable dictionary."""
        env_dict = {}
        for key, value in self.model_dump().items():
            env_key = f"{self.Config.env_prefix}{key.upper()}"
            if isinstance(value, Enum):
                env_dict[env_key] = value.value
            else:
                env_dict[env_key] = str(value)
        return env_dict


class HarmonyConfig(BaseSettings):
    """
    Harmony-specific configuration.
    
    Configuration for cross-platform testing with Harmony.
    """
    
    # Harmony server settings
    harmony_host: str = Field(
        default="127.0.0.1",
        description="Harmony server host"
    )
    
    harmony_port: int = Field(
        default=5006,
        ge=1024,
        le=65535,
        description="Harmony server port"
    )
    
    # Process configuration
    reader_platform: Platform = Field(
        default=Platform.PYTHON,
        description="Reader process platform"
    )
    
    writer_platform: Platform = Field(
        default=Platform.PYTHON,
        description="Writer process platform"
    )
    
    # Test selection
    test_pattern: str = Field(
        default="*",
        description="Test name pattern to run"
    )
    
    test_timeout_seconds: int = Field(
        default=30,
        ge=5,
        le=300,
        description="Overall test timeout in seconds"
    )
    
    # Discovery settings
    discovery_timeout_ms: int = Field(
        default=5000,
        ge=100,
        le=30000,
        description="Step discovery timeout in milliseconds"
    )
    
    # Communication settings
    json_rpc_timeout_ms: int = Field(
        default=10000,
        ge=1000,
        le=60000,
        description="JSON-RPC call timeout in milliseconds"
    )
    
    class Config:
        """Pydantic configuration."""
        env_prefix = "HARMONY_"
        env_file = ".env.harmony"
        env_file_encoding = "utf-8"
        case_sensitive = False


class ConfigManager:
    """
    Centralized configuration management.
    
    Provides singleton access to configuration with
    environment variable override support.
    """
    
    _test_config: Optional[TestConfig] = None
    _harmony_config: Optional[HarmonyConfig] = None
    
    @classmethod
    def get_test_config(cls) -> TestConfig:
        """
        Get test configuration singleton.
        
        Returns:
            TestConfig instance
        """
        if cls._test_config is None:
            cls._test_config = TestConfig()
        return cls._test_config
        
    @classmethod
    def get_harmony_config(cls) -> HarmonyConfig:
        """
        Get Harmony configuration singleton.
        
        Returns:
            HarmonyConfig instance
        """
        if cls._harmony_config is None:
            cls._harmony_config = HarmonyConfig()
        return cls._harmony_config
        
    @classmethod
    def reload(cls) -> None:
        """Reload configuration from environment."""
        cls._test_config = None
        cls._harmony_config = None
        
    @classmethod
    def override_test_config(cls, **overrides: Any) -> TestConfig:
        """
        Override test configuration values.
        
        Args:
            **overrides: Configuration overrides
            
        Returns:
            New TestConfig with overrides
        """
        config = cls.get_test_config()
        config_dict = config.model_dump()
        config_dict.update(overrides)
        cls._test_config = TestConfig(**config_dict)
        return cls._test_config
        
    @classmethod
    def from_file(cls, config_file: Path) -> TestConfig:
        """
        Load configuration from file.
        
        Args:
            config_file: Path to configuration file
            
        Returns:
            TestConfig loaded from file
        """
        if not config_file.exists():
            raise FileNotFoundError(f"Configuration file not found: {config_file}")
            
        # Set environment variable to load from file
        os.environ['ZEROBUFFER_TEST_ENV_FILE'] = str(config_file)
        cls._test_config = None
        return cls.get_test_config()


# Convenience function for getting config
def get_config() -> TestConfig:
    """
    Get the current test configuration.
    
    Returns:
        TestConfig instance
    """
    return ConfigManager.get_test_config()