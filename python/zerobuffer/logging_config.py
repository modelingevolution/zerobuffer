"""
Logging configuration for ZeroBuffer

Provides logging setup similar to C# ILogger pattern.
"""

import logging
import logging.config
from typing import Optional, Dict, Any


def get_logger(name: str) -> logging.Logger:
    """
    Get a logger instance for the given name.
    
    Similar to ILogger<T> in C#, uses module/class name for hierarchical logging.
    
    Args:
        name: Logger name, typically __name__ or module.class format
        
    Returns:
        Logger instance
    """
    return logging.getLogger(name)


def setup_logging(
    level: str = "INFO",
    log_file: Optional[str] = None,
    format_string: Optional[str] = None
) -> None:
    """
    Configure logging for the application.
    
    Similar to ConfigureLogging in C# startup.
    
    Args:
        level: Log level (DEBUG, INFO, WARNING, ERROR, CRITICAL)
        log_file: Optional file path for logging
        format_string: Optional custom format string
    """
    if format_string is None:
        format_string = "%(asctime)s [%(levelname)-8s] %(name)s: %(message)s"
    
    config: Dict[str, Any] = {
        'version': 1,
        'disable_existing_loggers': False,
        'formatters': {
            'standard': {
                'format': format_string,
                'datefmt': '%Y-%m-%d %H:%M:%S'
            },
            'detailed': {
                'format': '%(asctime)s.%(msecs)03d [%(levelname)-8s] %(name)s [%(filename)s:%(lineno)d]: %(message)s',
                'datefmt': '%Y-%m-%d %H:%M:%S'
            }
        },
        'handlers': {
            'console': {
                'class': 'logging.StreamHandler',
                'level': level,
                'formatter': 'standard',
                'stream': 'ext://sys.stdout'
            }
        },
        'root': {
            'level': 'DEBUG',  # Root captures all, handlers filter
            'handlers': ['console']
        },
        'loggers': {
            'zerobuffer': {
                'level': 'DEBUG',
                'handlers': ['console'],
                'propagate': False
            }
        }
    }
    
    # Add file handler if specified
    if log_file:
        config['handlers']['file'] = {
            'class': 'logging.FileHandler',
            'level': 'DEBUG',
            'formatter': 'detailed',
            'filename': log_file,
            'mode': 'a'
        }
        config['loggers']['zerobuffer']['handlers'].append('file')
    
    logging.config.dictConfig(config)


class LoggerMixin:
    """
    Mixin class to provide logger property.
    
    Similar to having ILogger<T> injected in C# classes.
    """
    
    @property
    def _logger(self) -> logging.Logger:
        """Get logger for this class"""
        if not hasattr(self, '_logger_instance'):
            # Use full module.class name like C# ILogger<T>
            self._logger_instance = logging.getLogger(
                f"{self.__class__.__module__}.{self.__class__.__name__}"
            )
        return self._logger_instance


# Null logger pattern (similar to NullLogger<T>.Instance in C#)
class NullLogger:
    """Null logger that does nothing, similar to NullLogger in C#"""
    
    def debug(self, msg, *args, **kwargs): pass
    def info(self, msg, *args, **kwargs): pass
    def warning(self, msg, *args, **kwargs): pass
    def error(self, msg, *args, **kwargs): pass
    def critical(self, msg, *args, **kwargs): pass
    def log(self, level, msg, *args, **kwargs): pass
    

NULL_LOGGER = NullLogger()