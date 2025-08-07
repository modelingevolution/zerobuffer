"""
Buffer naming service for test isolation

Ensures unique buffer names across test runs to prevent conflicts
when multiple tests run in parallel.
"""

import os
import time
import logging
from typing import Dict, Optional


class BufferNamingService:
    """Service for generating unique buffer names for test isolation"""
    
    def __init__(self, logger: Optional[logging.Logger] = None) -> None:
        """
        Initialize the buffer naming service
        
        Args:
            logger: Optional logger for debug output
        """
        self._logger = logger or logging.getLogger(__name__)
        self._name_cache: Dict[str, str] = {}
        
        # Check for Harmony environment variables
        harmony_pid = os.environ.get("HARMONY_HOST_PID")
        harmony_feature_id = os.environ.get("HARMONY_FEATURE_ID")
        
        if harmony_pid and harmony_feature_id:
            # Running under Harmony - use provided values for resource isolation
            self._test_run_id = f"{harmony_pid}_{harmony_feature_id}"
            self._logger.debug(f"Initialized with Harmony test run ID: {self._test_run_id}")
        else:
            # Running standalone - use process ID and timestamp for uniqueness
            pid = os.getpid()
            # Use nanoseconds for better uniqueness
            timestamp = int(time.time() * 1_000_000_000)
            self._test_run_id = f"{pid}_{timestamp}"
            self._logger.debug(f"Initialized with standalone test run ID: {self._test_run_id}")
    
    def get_buffer_name(self, base_name: str) -> str:
        """
        Get a unique buffer name for the given base name
        
        Args:
            base_name: The base buffer name from the test
            
        Returns:
            A unique buffer name that includes the test run ID
        """
        # Return cached name if we've seen this base name before
        if base_name in self._name_cache:
            cached_name = self._name_cache[base_name]
            # self._logger.debug(f"Returning cached buffer name: {cached_name} for base name: {base_name}")
            return cached_name
        
        # Create new unique name and cache it
        unique_name = f"{base_name}_{self._test_run_id}"
        self._name_cache[base_name] = unique_name
        
        # self._logger.debug(f"Created and cached buffer name: {unique_name} for base name: {base_name}")
        return unique_name
    
    def clear_cache(self) -> None:
        """Clear the name cache (useful for test cleanup)"""
        self._name_cache.clear()
        self._logger.debug("Cleared buffer name cache")