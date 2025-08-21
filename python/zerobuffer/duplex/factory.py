"""
Duplex Channel Factory implementation
"""

from typing import Optional, Any
from ..types import BufferConfig
from .interfaces import IDuplexChannelFactory, IImmutableDuplexServer, IDuplexClient
from .client import DuplexClient
from .server import ImmutableDuplexServer


class DuplexChannelFactory(IDuplexChannelFactory):
    """Factory for creating duplex channels"""
    
    _instance = None
    
    def __init__(self, logger_factory: Optional[Any] = None) -> None:
        """
        Create factory instance
        
        Args:
            logger_factory: Optional logger factory for creating loggers
        """
        self._logger_factory = logger_factory
    
    @classmethod
    def get_instance(cls) -> 'DuplexChannelFactory':
        """Get singleton instance"""
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance
    
    def create_immutable_server(self, channel_name: str, config: BufferConfig, timeout: Optional[float] = None) -> IImmutableDuplexServer:
        """
        Create an immutable server
        
        Args:
            channel_name: Name of the duplex channel
            config: Buffer configuration
            timeout: Optional timeout in seconds (None for default of 5 seconds)
            
        Returns:
            ImmutableDuplexServer instance
        """
        logger = None
        if self._logger_factory:
            logger = self._logger_factory.create_logger(f"ImmutableDuplexServer.{channel_name}")
        
        return ImmutableDuplexServer(channel_name, config, timeout, logger)
    
    def create_mutable_server(self, channel_name: str, config: BufferConfig) -> Any:
        """Create a mutable server (not yet implemented - planned for v2.0)"""
        raise NotImplementedError("MutableDuplexServer is planned for v2.0")
    
    def create_client(self, channel_name: str) -> IDuplexClient:
        """Connect to existing duplex channel"""
        return DuplexClient(channel_name)