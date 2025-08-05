#!/usr/bin/env python3
"""
ZeroBuffer Python Serve - JSON-RPC server for Harmony testing framework

This server implements the JSON-RPC protocol over stdin/stdout to execute
test steps for cross-platform ZeroBuffer testing.
"""

import asyncio
import logging
import sys
from pathlib import Path

# Add parent directory to path to import zerobuffer
sys.path.insert(0, str(Path(__file__).parent))

from zerobuffer_serve.server import ZeroBufferServe
from zerobuffer_serve.step_registry import StepRegistry
from zerobuffer_serve.test_context import TestContext
from zerobuffer_serve.logging.dual_logger import DualLoggerProvider
from zerobuffer_serve.step_definitions import (
    BasicCommunicationSteps,
    BenchmarksSteps,
    EdgeCasesSteps,
    ErrorHandlingSteps,
    InitializationSteps,
    PerformanceSteps,
    ProcessLifecycleSteps,
    StressTestsSteps,
    SynchronizationSteps,
)


async def main():
    """Main entry point for the serve application"""
    # Configure stderr logging for debugging
    logging.basicConfig(
        level=logging.DEBUG,
        format='[%(asctime)s.%(msecs)03d] %(name)s: %(message)s',
        datefmt='%Y-%m-%d %H:%M:%S',
        stream=sys.stderr
    )
    
    # Log startup
    logger = logging.getLogger(__name__)
    logger.info("ZeroBuffer Python Serve starting...")
    
    # Create dual logger provider
    logger_provider = DualLoggerProvider()
    
    # Create test context
    test_context = TestContext()
    
    # Create and configure step registry
    step_registry = StepRegistry(logger_provider.get_logger("StepRegistry"))
    
    # Register all step definition classes
    step_classes = [
        BasicCommunicationSteps,
        BenchmarksSteps,
        EdgeCasesSteps,
        ErrorHandlingSteps,
        InitializationSteps,
        PerformanceSteps,
        ProcessLifecycleSteps,
        StressTestsSteps,
        SynchronizationSteps,
    ]
    
    for step_class in step_classes:
        instance = step_class(test_context, logger_provider.get_logger(step_class.__name__))
        step_registry.register_instance(instance)
    
    # Discover all steps
    step_registry.discover_steps()
    logger.info(f"Discovered {len(step_registry.get_all_steps())} step definitions")
    
    # Create and run server
    server = ZeroBufferServe(
        step_registry=step_registry,
        test_context=test_context,
        logger_provider=logger_provider
    )
    
    try:
        await server.run()
    except Exception as e:
        logger.error(f"Fatal error: {e}", exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    # Run the async main function
    asyncio.run(main())