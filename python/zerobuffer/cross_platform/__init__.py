"""
Cross-platform test applications for ZeroBuffer.

This module provides standardized test applications for interoperability testing
between C++, C#, and Python implementations.
"""

from .writer import main as writer_main
from .reader import main as reader_main
from .relay import main as relay_main

__all__ = ['writer_main', 'reader_main', 'relay_main']