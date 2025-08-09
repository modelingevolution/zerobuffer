"""
Data models for JSON-RPC requests and responses

These models match the C# implementation to ensure cross-platform compatibility.
"""

from dataclasses import dataclass, field
from typing import List, Dict, Any, Optional


# Health check is now parameterless according to Harmony contract
# HealthRequest removed - health check should be parameterless


@dataclass
class InitializeRequest:
    """Process initialization request - matches Harmony contract"""
    # Required fields from Harmony contract
    role: str = ""
    platform: str = ""
    scenario: str = ""
    hostPid: int = 0
    featureId: int = 0
    
    @property
    def testRunId(self) -> str:
        """Computed property matching C# contract"""
        return f"{self.hostPid}_{self.featureId}"


@dataclass
class StepRequest:
    """Step execution request - matches Harmony contract"""
    # Required fields from Harmony contract
    process: str = ""
    stepType: str = ""
    step: str = ""
    parameters: Optional[Dict[str, str]] = None  # Changed to str,str as per contract
    context: Optional[Dict[str, str]] = None  # Added context field
    isBroadcast: bool = False
    
    # Legacy fields for backward compatibility (not in contract)
    originalStep: Optional[str] = None
    table: Optional['TableData'] = None


@dataclass
class TableData:
    """Table data for steps with tabular input"""
    headers: List[str] = field(default_factory=list)
    rows: List[Dict[str, str]] = field(default_factory=list)


@dataclass
class LogEntry:
    """Log entry for step execution"""
    level: str = "INFO"
    message: str = ""

@dataclass
class LogResponse:
    """Log response matching Harmony contract"""
    timestamp: str = ""  # ISO format timestamp
    level: int = 2  # Microsoft.Extensions.Logging.LogLevel enum value (2 = Information)
    message: str = ""


@dataclass
class StepResponse:
    """Step execution response - matches Harmony contract"""
    success: bool = True
    error: Optional[str] = None
    context: Optional[Dict[str, str]] = None  # Changed from data to context
    logs: Optional[List[LogResponse]] = None  # Changed to LogResponse type
    
    # Legacy field for backward compatibility
    data: Optional[Dict[str, Any]] = None


@dataclass
class StepInfo:
    """Step definition information for discovery"""
    type: str = ""  # "given", "when", "then"
    pattern: str = ""  # Regex pattern


@dataclass
class DiscoverResponse:
    """Step discovery response"""
    steps: List[StepInfo] = field(default_factory=list)