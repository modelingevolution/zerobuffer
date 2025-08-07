"""
Base class for step definitions

Provides common functionality for all step definition classes.
"""

import logging
from typing import Dict, Any, Optional

from ..test_context import TestContext
from ..step_registry import given, when, then


class BaseSteps:
    """Base class for step definitions"""
    
    def __init__(self, test_context: TestContext, logger: logging.Logger) -> None:
        self._context = test_context
        self._logger = logger
        
    @property
    def context(self) -> TestContext:
        """Get the test context"""
        return self._context
        
    @property
    def logger(self) -> logging.Logger:
        """Get the logger"""
        return self._logger
        
    def store_resource(self, name: str, resource: Any) -> None:
        """Store a resource in the context for cleanup"""
        self._context.add_resource(name, resource)
        
    def get_resource(self, name: str) -> Optional[Any]:
        """Get a resource from the context"""
        return self._context.get_resource(name)
        
    def set_data(self, key: str, value: Any) -> None:
        """Store data in the context"""
        self._context.set_data(key, value)
        
    def get_data(self, key: str, default: Any = None) -> Any:
        """Get data from the context"""
        return self._context.get_data(key, default)