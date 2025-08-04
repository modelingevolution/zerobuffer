"""
Duplex Channel Factory implementation
"""

from ..types import BufferConfig
from .interfaces import IDuplexChannelFactory, IImmutableDuplexServer, IMutableDuplexServer, IDuplexClient
from .client import DuplexClient
from .server import ImmutableDuplexServer, MutableDuplexServer


class DuplexChannelFactory(IDuplexChannelFactory):
    """Factory for creating duplex channels"""
    
    _instance = None
    
    def __init__(self, logger_factory=None):
        """
        Create factory instance
        
        Args:
            logger_factory: Optional logger factory for creating loggers
        """
        self._logger_factory = logger_factory
    
    @classmethod
    def get_instance(cls):
        """Get singleton instance"""
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance
    
    def create_immutable_server(self, channel_name: str, config: BufferConfig) -> IImmutableDuplexServer:
        """Create an immutable server"""
        logger = None
        if self._logger_factory:
            logger = self._logger_factory.create_logger(f"ImmutableDuplexServer.{channel_name}")
        
        return ImmutableDuplexServer(channel_name, config, logger)
    
    def create_mutable_server(self, channel_name: str, config: BufferConfig) -> IMutableDuplexServer:
        """Create a mutable server"""
        logger = None
        if self._logger_factory:
            logger = self._logger_factory.create_logger(f"MutableDuplexServer.{channel_name}")
        
        return MutableDuplexServer(channel_name, config, logger)
    
    def create_client(self, channel_name: str) -> IDuplexClient:
        """Connect to existing duplex channel"""
        return DuplexClient(channel_name)