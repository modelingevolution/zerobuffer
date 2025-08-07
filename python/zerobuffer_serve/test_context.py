"""
Test context for managing state during test execution

Similar to ITestContext in the C# implementation.
"""

from typing import Dict, Any, Optional
import logging


class TestContext:
    """Manages test execution context and shared state"""
    
    def __init__(self) -> None:
        self._role: Optional[str] = None
        self._platform: Optional[str] = None
        self._scenario: Optional[str] = None
        self._test_run_id: Optional[str] = None
        self._data: Dict[str, Any] = {}
        self._resources: Dict[str, Any] = {}
        self._logger = logging.getLogger(self.__class__.__name__)
        
    def initialize(
        self,
        role: str,
        platform: str,
        scenario: str,
        test_run_id: str
    ) -> None:
        """Initialize the test context"""
        self._role = role
        self._platform = platform
        self._scenario = scenario
        self._test_run_id = test_run_id
        
        self._logger.info(
            f"Test context initialized: role={role}, platform={platform}, "
            f"scenario={scenario}, test_run_id={test_run_id}"
        )
        
    @property
    def role(self) -> Optional[str]:
        """Get the process role (reader/writer)"""
        return self._role
        
    @property
    def platform(self) -> Optional[str]:
        """Get the platform name"""
        return self._platform
        
    @property
    def scenario(self) -> Optional[str]:
        """Get the current scenario name"""
        return self._scenario
        
    @property
    def test_run_id(self) -> Optional[str]:
        """Get the test run ID"""
        return self._test_run_id
        
    def set_data(self, key: str, value: Any) -> None:
        """Store arbitrary data in the context"""
        self._data[key] = value
        self._logger.debug(f"Set context data: {key} = {value}")
        
    def get_data(self, key: str, default: Any = None) -> Any:
        """Retrieve data from the context"""
        return self._data.get(key, default)
        
    def add_resource(self, name: str, resource: Any) -> None:
        """Add a resource to be cleaned up later"""
        self._resources[name] = resource
        self._logger.debug(f"Added resource: {name}")
        
    def get_resource(self, name: str) -> Optional[Any]:
        """Get a resource by name"""
        return self._resources.get(name)
        
    def cleanup(self) -> None:
        """Clean up all resources"""
        self._logger.info("Cleaning up test context resources")
        
        for name, resource in self._resources.items():
            try:
                # Try to call cleanup/close/dispose methods
                if hasattr(resource, 'cleanup'):
                    resource.cleanup()
                elif hasattr(resource, 'close'):
                    resource.close()
                elif hasattr(resource, 'dispose'):
                    resource.dispose()
                    
                self._logger.debug(f"Cleaned up resource: {name}")
            except Exception as e:
                self._logger.error(f"Failed to cleanup resource {name}: {e}")
                
        # Clear all data
        self._resources.clear()
        self._data.clear()
        
    def reset(self) -> None:
        """Reset context for a new test"""
        self.cleanup()
        self._role = None
        self._platform = None
        self._scenario = None
        self._test_run_id = None