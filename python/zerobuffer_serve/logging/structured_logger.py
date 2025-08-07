"""
Structured logging configuration for ZeroBuffer tests.

Provides JSON-structured logging with correlation IDs, trace context,
and proper log levels for production environments.
"""

import json
import logging
import sys
import time
import uuid
from contextlib import contextmanager
from contextvars import ContextVar
from datetime import datetime, timezone
from typing import Any, Dict, Optional, Union
from logging import LogRecord


# Context variables for correlation
correlation_id: ContextVar[Optional[str]] = ContextVar('correlation_id', default=None)
trace_id: ContextVar[Optional[str]] = ContextVar('trace_id', default=None)
span_id: ContextVar[Optional[str]] = ContextVar('span_id', default=None)
test_id: ContextVar[Optional[str]] = ContextVar('test_id', default=None)


class StructuredFormatter(logging.Formatter):
    """
    JSON-structured log formatter with trace context.
    
    Produces logs in a format suitable for log aggregation systems
    like ELK stack, Datadog, or CloudWatch.
    """
    
    def __init__(
        self,
        include_timestamp: bool = True,
        include_level: bool = True,
        include_location: bool = True,
        include_trace: bool = True
    ):
        """
        Initialize StructuredFormatter.
        
        Args:
            include_timestamp: Include timestamp in logs
            include_level: Include log level
            include_location: Include file/line information
            include_trace: Include trace context
        """
        super().__init__()
        self.include_timestamp = include_timestamp
        self.include_level = include_level
        self.include_location = include_location
        self.include_trace = include_trace
        
    def format(self, record: LogRecord) -> str:
        """
        Format log record as JSON.
        
        Args:
            record: Log record to format
            
        Returns:
            JSON-formatted log string
        """
        log_data: Dict[str, Any] = {
            "message": record.getMessage()
        }
        
        # Add timestamp
        if self.include_timestamp:
            log_data["timestamp"] = datetime.fromtimestamp(
                record.created, 
                tz=timezone.utc
            ).isoformat()
            
        # Add level
        if self.include_level:
            log_data["level"] = record.levelname
            log_data["level_number"] = record.levelno
            
        # Add location information
        if self.include_location:
            log_data["logger"] = record.name
            log_data["file"] = record.pathname
            log_data["line"] = record.lineno
            log_data["function"] = record.funcName
            
        # Add trace context
        if self.include_trace:
            if cid := correlation_id.get():
                log_data["correlation_id"] = cid
            if tid := trace_id.get():
                log_data["trace_id"] = tid
            if sid := span_id.get():
                log_data["span_id"] = sid
            if test := test_id.get():
                log_data["test_id"] = test
                
        # Add any extra fields from the record
        if hasattr(record, 'extra_fields'):
            log_data.update(record.extra_fields)
            
        # Add exception info if present
        if record.exc_info:
            log_data["exception"] = self.formatException(record.exc_info)
            
        return json.dumps(log_data, default=str)


class StructuredLogger(logging.LoggerAdapter[logging.Logger]):
    """
    Structured logger with automatic context injection.
    
    Provides a fluent interface for structured logging with
    automatic inclusion of trace context and extra fields.
    """
    
    def __init__(self, logger: logging.Logger, extra: Optional[Dict[str, Any]] = None) -> None:
        """
        Initialize StructuredLogger.
        
        Args:
            logger: Base logger instance
            extra: Default extra fields to include
        """
        super().__init__(logger, extra or {})
        
    def process(self, msg: Any, kwargs: Any) -> tuple[Any, Any]:
        """
        Process log message and inject context.
        
        Args:
            msg: Log message
            kwargs: Keyword arguments
            
        Returns:
            Processed message and kwargs
        """
        # Extract extra fields
        extra_fields = kwargs.pop('extra', {})
        
        # Add adapter's extra fields
        if self.extra:
            extra_fields.update(self.extra)
            
        # Create new record with extra_fields attribute
        kwargs['extra'] = {'extra_fields': extra_fields}
        
        return msg, kwargs
        
    def with_context(self, **context: Any) -> 'StructuredLogger':
        """
        Create a new logger with additional context.
        
        Args:
            **context: Additional context fields
            
        Returns:
            New StructuredLogger with merged context
        """
        new_extra = dict(self.extra) if self.extra else {}
        new_extra.update(context)
        return StructuredLogger(self.logger, new_extra)
        
    @contextmanager
    def operation(self, operation_name: str, **context: Any) -> Any:
        """
        Context manager for logging operations.
        
        Args:
            operation_name: Name of the operation
            **context: Additional context for the operation
            
        Yields:
            None
        """
        start_time = time.perf_counter()
        op_id = str(uuid.uuid4())[:8]
        
        self.info(
            f"Starting {operation_name}",
            extra={
                "operation": operation_name,
                "operation_id": op_id,
                "operation_status": "started",
                **context
            }
        )
        
        try:
            yield
            duration = time.perf_counter() - start_time
            self.info(
                f"Completed {operation_name}",
                extra={
                    "operation": operation_name,
                    "operation_id": op_id,
                    "operation_status": "completed",
                    "duration_seconds": duration,
                    **context
                }
            )
        except Exception as e:
            duration = time.perf_counter() - start_time
            self.error(
                f"Failed {operation_name}: {e}",
                extra={
                    "operation": operation_name,
                    "operation_id": op_id,
                    "operation_status": "failed",
                    "duration_seconds": duration,
                    "error_type": type(e).__name__,
                    **context
                },
                exc_info=True
            )
            raise


class LoggerFactory:
    """
    Factory for creating configured loggers.
    
    Centralizes logger configuration and ensures consistent
    formatting across the application.
    """
    
    _formatter: Optional[StructuredFormatter] = None
    _handler: Optional[logging.Handler] = None
    _log_level: int = logging.INFO
    
    @classmethod
    def configure(
        cls,
        level: Union[str, int] = logging.INFO,
        output: str = "stdout",
        format_json: bool = True
    ) -> None:
        """
        Configure global logging settings.
        
        Args:
            level: Log level (string or int)
            output: Output destination (stdout/stderr/file path)
            format_json: Use JSON formatting
        """
        # Set log level
        if isinstance(level, str):
            cls._log_level = getattr(logging, level.upper())
        else:
            cls._log_level = level
            
        # Create formatter
        if format_json:
            cls._formatter = StructuredFormatter()
        else:
            # For non-JSON formatting, we still store as StructuredFormatter type
            # but disable its JSON features
            cls._formatter = StructuredFormatter(
                include_timestamp=True,
                include_level=True,
                include_location=False,
                include_trace=False
            )
            
        # Create handler
        if output == "stdout":
            cls._handler = logging.StreamHandler(sys.stdout)
        elif output == "stderr":
            cls._handler = logging.StreamHandler(sys.stderr)
        else:
            cls._handler = logging.FileHandler(output)
            
        cls._handler.setFormatter(cls._formatter)
        
    @classmethod
    def get_logger(
        cls,
        name: str,
        extra: Optional[Dict[str, Any]] = None
    ) -> StructuredLogger:
        """
        Get a configured logger instance.
        
        Args:
            name: Logger name
            extra: Default extra fields
            
        Returns:
            Configured StructuredLogger
        """
        # Ensure we're configured
        if cls._formatter is None:
            cls.configure()
            
        # Create base logger
        logger = logging.getLogger(name)
        logger.setLevel(cls._log_level)
        
        # Remove existing handlers to avoid duplicates
        logger.handlers.clear()
        
        # Add our handler
        if cls._handler:
            logger.addHandler(cls._handler)
            
        # Return wrapped logger
        return StructuredLogger(logger, extra)


def with_correlation(correlation: Optional[str] = None) -> Any:
    """
    Context manager to set correlation ID.
    
    Args:
        correlation: Correlation ID (auto-generated if None)
        
    Returns:
        Context manager that yields correlation ID
    """
    @contextmanager
    def _context() -> Any:
        cid = correlation or str(uuid.uuid4())
        token = correlation_id.set(cid)
        try:
            yield cid
        finally:
            correlation_id.reset(token)
    return _context()


def with_trace(
    trace: Optional[str] = None,
    span: Optional[str] = None
) -> Any:
    """
    Context manager to set trace context.
    
    Args:
        trace: Trace ID (auto-generated if None)
        span: Span ID (auto-generated if None)
        
    Returns:
        Context manager that yields tuple of (trace_id, span_id)
    """
    @contextmanager
    def _context() -> Any:
        tid = trace or str(uuid.uuid4())
        sid = span or str(uuid.uuid4())[:16]
        
        trace_token = trace_id.set(tid)
        span_token = span_id.set(sid)
        
        try:
            yield (tid, sid)
        finally:
            trace_id.reset(trace_token)
            span_id.reset(span_token)
    return _context()


def with_test_context(test_name: str) -> Any:
    """
    Context manager for test execution context.
    
    Args:
        test_name: Name of the test
        
    Returns:
        Context manager that yields test ID
    """
    @contextmanager
    def _context() -> Any:
        tid = f"{test_name}_{str(uuid.uuid4())[:8]}"
        token = test_id.set(tid)
        try:
            yield tid
        finally:
            test_id.reset(token)
    return _context()