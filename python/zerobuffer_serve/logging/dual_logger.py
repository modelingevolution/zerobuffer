"""
Dual logging system that logs to stderr and collects logs in memory

Similar to the C# DualLogger implementation.
"""

import logging
import sys
from typing import List, Dict, Any, Optional, Mapping
from threading import Lock

from ..models import LogEntry


class LogCollector:
    """Collects log entries in memory for JSON-RPC responses"""
    
    def __init__(self) -> None:
        self._logs: List[LogEntry] = []
        self._lock = Lock()
        
    def add(self, level: str, message: str) -> None:
        """Add a log entry"""
        with self._lock:
            self._logs.append(LogEntry(level=level, message=message))
            
    def get_and_clear(self) -> List[LogEntry]:
        """Get all logs and clear the collection"""
        with self._lock:
            logs = self._logs.copy()
            self._logs.clear()
            return logs


class DualLogger(logging.Logger):
    """Logger that writes to stderr and collects in memory"""
    
    def __init__(self, name: str, collector: LogCollector) -> None:
        super().__init__(name)
        self._collector = collector
        
    def _log(self, level: int, msg: object, args: Any, 
             exc_info: Any = None, extra: Optional[Mapping[str, object]] = None, 
             stack_info: bool = False, stacklevel: int = 1) -> None:
        """Override to capture logs"""
        # Call parent to handle normal logging
        super()._log(level, msg, args, exc_info, extra, stack_info, stacklevel)
        
        # Also collect the log
        level_name = logging.getLevelName(level)
        formatted_msg = str(msg) % args if args else str(msg)
        self._collector.add(level_name, formatted_msg)


class DualLoggerProvider:
    """Provides dual loggers with shared log collection"""
    
    def __init__(self) -> None:
        self._collector = LogCollector()
        self._loggers: Dict[str, logging.Logger] = {}
        
        # Configure stderr handler
        handler = logging.StreamHandler(sys.stderr)
        handler.setLevel(logging.DEBUG)
        formatter = logging.Formatter(
            '[%(asctime)s.%(msecs)03d] %(name)s: %(message)s',
            datefmt='%Y-%m-%d %H:%M:%S'
        )
        handler.setFormatter(formatter)
        self._handler = handler
        
    def get_logger(self, name: str) -> logging.Logger:
        """Get or create a dual logger"""
        if name not in self._loggers:
            # Create custom logger class instance
            logger = DualLogger(f"zerobuffer.serve.{name}", self._collector)
            logger.setLevel(logging.DEBUG)
            logger.addHandler(self._handler)
            logger.propagate = False
            self._loggers[name] = logger
            
        return self._loggers[name]
        
    def get_all_logs(self) -> List[LogEntry]:
        """Get all collected logs and clear"""
        return self._collector.get_and_clear()