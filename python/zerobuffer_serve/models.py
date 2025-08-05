"""
Data models for JSON-RPC requests and responses

These models match the C# implementation to ensure cross-platform compatibility.
"""

from dataclasses import dataclass, field
from typing import List, Dict, Any, Optional


@dataclass
class HealthRequest:
    """Health check request"""
    hostPid: int = 0
    featureId: int = 0


@dataclass
class InitializeRequest:
    """Process initialization request"""
    # Harmony ProcessManager parameters
    hostPid: int = 0
    featureId: int = 0
    
    # Test context parameters
    role: str = ""
    platform: str = ""
    scenario: str = ""
    testRunId: str = ""


@dataclass
class StepRequest:
    """Step execution request"""
    process: str = ""
    stepType: str = ""
    step: str = ""
    originalStep: str = ""
    parameters: Optional[Dict[str, Any]] = None
    isBroadcast: bool = False
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
class StepResponse:
    """Step execution response"""
    success: bool = True
    error: Optional[str] = None
    data: Dict[str, Any] = field(default_factory=dict)
    logs: List[LogEntry] = field(default_factory=list)


@dataclass
class StepInfo:
    """Step definition information for discovery"""
    type: str = ""  # "given", "when", "then"
    pattern: str = ""  # Regex pattern


@dataclass
class DiscoverResponse:
    """Step discovery response"""
    steps: List[StepInfo] = field(default_factory=list)