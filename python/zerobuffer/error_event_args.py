"""
Error event arguments for exception handling

Provides a way to pass exception information through event handlers,
matching the C# ErrorEventArgs pattern.
"""


class ErrorEventArgs:
    """Event arguments for error events"""

    def __init__(self, exception: Exception) -> None:
        """
        Create error event arguments

        Args:
            exception: The exception that occurred
        """
        if not isinstance(exception, Exception):
            raise TypeError("exception must be an Exception instance")
        self._exception = exception

    @property
    def exception(self) -> Exception:
        """Get the exception"""
        return self._exception

    def __str__(self) -> str:
        """String representation"""
        return f"ErrorEventArgs({type(self._exception).__name__}: {self._exception})"

    def __repr__(self) -> str:
        """Detailed representation"""
        return f"ErrorEventArgs(exception={self._exception!r})"
