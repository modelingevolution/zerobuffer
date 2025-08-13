#!/usr/bin/env python3
"""
Main entry point for ZeroBuffer Serve
Provides Harmony-compatible JSON-RPC server for test orchestration
"""

import asyncio
import sys
import logging
from .server import ZeroBufferServe
from .step_registry import StepRegistry
from .test_context import TestContext
from .logging.dual_logger import DualLoggerProvider


async def main() -> None:
    """Main entry point for the serve application"""
    # Disable the root logger to prevent duplicate logging
    # DualLoggerProvider will handle all logging to stderr
    logging.getLogger().handlers = []
    logging.getLogger().setLevel(logging.CRITICAL)
    
    # Create dependencies
    logger_provider = DualLoggerProvider()
    test_context = TestContext()
    
    # Create a logger for the step registry using the DualLoggerProvider
    main_logger = logger_provider.get_logger("StepRegistry")
    step_registry = StepRegistry(main_logger)
    
    # Import and register all step definitions
    from .step_definitions import (
        BasicCommunicationSteps,
        BenchmarksSteps,
        EdgeCasesSteps,
        ErrorHandlingSteps,
        InitializationSteps,
        PerformanceSteps,
        ProcessLifecycleSteps,
        StressTestsSteps,
        SynchronizationSteps
    )
    
    # Instantiate and register step definition classes
    # Create a logger for step definitions
    step_logger = logger_provider.get_logger("StepDefinitions")
    
    step_registry.register_instance(BasicCommunicationSteps(test_context, step_logger))
    step_registry.register_instance(BenchmarksSteps(test_context, step_logger))
    step_registry.register_instance(EdgeCasesSteps(test_context, step_logger))
    step_registry.register_instance(ErrorHandlingSteps(test_context, step_logger))
    step_registry.register_instance(InitializationSteps(test_context, step_logger))
    step_registry.register_instance(PerformanceSteps(test_context, step_logger))
    step_registry.register_instance(ProcessLifecycleSteps(test_context, step_logger))
    step_registry.register_instance(StressTestsSteps(test_context, step_logger))
    step_registry.register_instance(SynchronizationSteps(test_context, step_logger))
    
    # Discover all steps from registered instances
    step_registry.discover_steps()
    
    # Create and run server
    server = ZeroBufferServe(step_registry, test_context, logger_provider)
    
    try:
        await server.run()
    except KeyboardInterrupt:
        pass
    except Exception as e:
        logging.error(f"Server error: {e}", exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    asyncio.run(main())