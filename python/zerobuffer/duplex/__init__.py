"""
ZeroBuffer Duplex Channel implementation

Provides request-response communication patterns on top of ZeroBuffer.
"""

from .interfaces import (
    IDuplexClient,
    IDuplexServer,
    IImmutableDuplexServer,
    IMutableDuplexServer,
    IDuplexChannelFactory,
    DuplexResponse
)

from .factory import DuplexChannelFactory
from .client import DuplexClient
from .server import ImmutableDuplexServer, MutableDuplexServer
from .processing_mode import ProcessingMode

__all__ = [
    'IDuplexClient',
    'IDuplexServer', 
    'IImmutableDuplexServer',
    'IMutableDuplexServer',
    'IDuplexChannelFactory',
    'DuplexResponse',
    'DuplexChannelFactory',
    'DuplexClient',
    'ImmutableDuplexServer',
    'MutableDuplexServer',
    'ProcessingMode'
]