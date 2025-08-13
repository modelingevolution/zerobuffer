"""
Data models for JSON-RPC requests and responses matching Harmony.Shared contracts.

These models provide exact compatibility with the C# Harmony.Shared library for 
enterprise-grade cross-platform servo communication.
"""

from dataclasses import dataclass, field
from typing import List, Dict, Optional


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
    """Step execution request - matches Harmony.Shared.StepRequest exactly"""
    process: str = ""
    stepType: str = ""  # Expects "Given", "When", or "Then"
    step: str = ""
    parameters: Optional[Dict[str, str]] = None
    context: Optional[Dict[str, str]] = None
    isBroadcast: bool = False



@dataclass
class LogResponse:
    """Log response matching Harmony contract"""
    timestamp: str = ""  # ISO format timestamp
    level: int = 2  # Microsoft.Extensions.Logging.LogLevel enum value (2 = Information)
    message: str = ""


@dataclass
class StepResponse:
    """Step execution response - matches Harmony.Shared.StepResponse exactly"""
    success: bool = True
    error: Optional[str] = None
    context: Optional[Dict[str, str]] = None
    logs: Optional[List[LogResponse]] = None


@dataclass
class StepInfo:
    """Step definition information - matches Harmony.Shared.StepInfo"""
    type: str = ""  # "Given", "When", or "Then" (capitalized)
    pattern: str = ""  # Regex pattern for step matching


@dataclass
class DiscoverResponse:
    """Step discovery response - matches Harmony.Shared.DiscoverResponse"""
    steps: List[StepInfo] = field(default_factory=list)