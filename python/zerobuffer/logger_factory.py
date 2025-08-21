"""
Logger factory for ZeroBuffer

Provides a factory pattern for creating loggers, similar to C#'s ILoggerFactory.
"""

import logging
from typing import Optional, Dict, Any, List
from abc import ABC, abstractmethod


class ILoggerFactory(ABC):
    """Abstract base class for logger factories"""
    
    @abstractmethod
    def create_logger(self, name: str) -> logging.Logger:
        """
        Create a logger with the given name
        
        Args:
            name: Logger name (typically module or class name)
            
        Returns:
            Configured logger instance
        """
        pass


class LoggerFactory(ILoggerFactory):
    """Default logger factory implementation"""
    
    def __init__(self, 
                 level: int = logging.INFO,
                 format_string: Optional[str] = None,
                 handlers: Optional[List[Any]] = None):
        """
        Create a logger factory
        
        Args:
            level: Default logging level
            format_string: Log message format string
            handlers: Optional list of handlers to add to loggers
        """
        self._level = level
        self._format_string = format_string or '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
        self._handlers = handlers or []
        self._formatter = logging.Formatter(self._format_string)
        
    def create_logger(self, name: str) -> logging.Logger:
        """Create a configured logger"""
        logger = logging.getLogger(name)
        logger.setLevel(self._level)
        
        # Only add handlers if the logger doesn't have any
        if not logger.handlers:
            # Add default console handler if no handlers specified
            if not self._handlers:
                handler = logging.StreamHandler()
                handler.setFormatter(self._formatter)
                logger.addHandler(handler)
            else:
                # Add specified handlers
                for handler in self._handlers:
                    if not any(isinstance(h, type(handler)) for h in logger.handlers):
                        handler.setFormatter(self._formatter)
                        logger.addHandler(handler)
        
        return logger


class NullLoggerFactory(ILoggerFactory):
    """Factory that creates loggers with NullHandler (no output)"""
    
    def create_logger(self, name: str) -> logging.Logger:
        """Create a logger that discards all messages"""
        logger = logging.getLogger(name)
        logger.addHandler(logging.NullHandler())
        return logger


# Singleton default factory
_default_factory: Optional[ILoggerFactory] = None


def get_default_factory() -> ILoggerFactory:
    """Get the default logger factory"""
    global _default_factory
    if _default_factory is None:
        _default_factory = LoggerFactory()
    return _default_factory


def set_default_factory(factory: ILoggerFactory) -> None:
    """Set the default logger factory"""
    global _default_factory
    _default_factory = factory