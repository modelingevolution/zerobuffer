#!/usr/bin/env python3
"""
Main entry point for ZeroBuffer Serve
Provides Harmony-compatible JSON-RPC server for test orchestration
"""

import asyncio
import sys
import logging
from .server import ZeroBufferServe


async def main():
    """Main entry point for the serve application"""
    # Set up logging to stderr so it doesn't interfere with JSON-RPC on stdout
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        stream=sys.stderr
    )
    
    # Create and run server
    server = ZeroBufferServe()
    
    try:
        await server.run()
    except KeyboardInterrupt:
        pass
    except Exception as e:
        logging.error(f"Server error: {e}", exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    asyncio.run(main())