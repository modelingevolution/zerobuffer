"""
Processing mode for duplex servers
"""

from enum import Enum


class ProcessingMode(Enum):
    """Defines how the server processes incoming requests"""
    
    SINGLE_THREAD = "single_thread"  # Process requests sequentially in one background thread
    THREAD_POOL = "thread_pool"      # Process each request in a thread pool (not yet implemented)