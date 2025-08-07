"""
Async step execution management for ZeroBuffer tests.

Provides proper async/await handling with timeouts, cleanup,
and concurrent task management.
"""

import asyncio
import functools
import time
from contextlib import asynccontextmanager
from typing import (
    Any, Callable, Coroutine, Dict, List, Optional, 
    TypeVar, Union, Set, AsyncGenerator
)
from dataclasses import dataclass, field
from asyncio import Task, Event, Lock, Queue
import logging

from .config import TestConfig, ConfigManager


T = TypeVar('T')


class StepTimeoutException(Exception):
    """Step execution timed out."""
    pass


class AsyncCleanupException(Exception):
    """Error during async cleanup."""
    pass


@dataclass
class TaskInfo:
    """Information about a running task."""
    task: Task[Any]
    name: str
    started_at: float
    timeout: Optional[float] = None
    
    @property
    def elapsed(self) -> float:
        """Time elapsed since task started."""
        return time.perf_counter() - self.started_at
        
    @property
    def is_timeout(self) -> bool:
        """Check if task has timed out."""
        if self.timeout is None:
            return False
        return self.elapsed > self.timeout


class AsyncStepManager:
    """
    Manage async step execution with proper cleanup.
    
    Ensures all async operations are properly tracked, timed,
    and cleaned up even in case of failures.
    """
    
    def __init__(
        self,
        logger: logging.Logger,
        config: Optional[TestConfig] = None
    ) -> None:
        """
        Initialize AsyncStepManager.
        
        Args:
            logger: Logger for operations
            config: Test configuration
        """
        self._logger = logger
        self._config = config or ConfigManager.get_test_config()
        self._tasks: Dict[str, TaskInfo] = {}
        self._cleanup_event = Event()
        self._lock = Lock()
        self._cleanup_callbacks: List[Callable[[], Any]] = []
        self._task_counter = 0
        
    async def run_step_with_timeout(
        self,
        coro: Coroutine[Any, Any, T],
        timeout: Optional[float] = None,
        step_name: Optional[str] = None
    ) -> T:
        """
        Run step with timeout and cleanup.
        
        Args:
            coro: Coroutine to execute
            timeout: Timeout in seconds (uses config default if None)
            step_name: Name of the step for tracking
            
        Returns:
            Result of the coroutine
            
        Raises:
            StepTimeoutException: If step times out
        """
        if timeout is None:
            timeout = self._config.buffer_timeout_ms / 1000.0
            
        # Generate task name if not provided
        if step_name is None:
            self._task_counter += 1
            step_name = f"step_{self._task_counter}"
            
        # Create and track task
        task = asyncio.create_task(coro)
        task_info = TaskInfo(
            task=task,
            name=step_name,
            started_at=time.perf_counter(),
            timeout=timeout
        )
        
        async with self._lock:
            self._tasks[step_name] = task_info
            
        self._logger.debug(
            f"Starting async step: {step_name}",
            extra={
                "step_name": step_name,
                "timeout": timeout
            }
        )
        
        try:
            result = await asyncio.wait_for(task, timeout)
            
            self._logger.debug(
                f"Completed async step: {step_name}",
                extra={
                    "step_name": step_name,
                    "duration": task_info.elapsed
                }
            )
            
            return result
            
        except asyncio.TimeoutError:
            self._logger.error(
                f"Step timed out: {step_name}",
                extra={
                    "step_name": step_name,
                    "timeout": timeout
                }
            )
            task.cancel()
            raise StepTimeoutException(
                f"Step '{step_name}' timed out after {timeout}s"
            )
            
        except Exception as e:
            self._logger.error(
                f"Step failed: {step_name}",
                extra={
                    "step_name": step_name,
                    "error": str(e),
                    "duration": task_info.elapsed
                }
            )
            raise
            
        finally:
            async with self._lock:
                self._tasks.pop(step_name, None)
                
    async def run_concurrent_steps(
        self,
        *coros: Coroutine,
        timeout: Optional[float] = None,
        return_exceptions: bool = False
    ) -> List[Any]:
        """
        Run multiple steps concurrently.
        
        Args:
            *coros: Coroutines to run concurrently
            timeout: Overall timeout for all steps
            return_exceptions: Return exceptions instead of raising
            
        Returns:
            List of results in same order as input
            
        Raises:
            StepTimeoutException: If overall timeout exceeded
        """
        if timeout is None:
            timeout = self._config.buffer_timeout_ms / 1000.0
            
        tasks = []
        for i, coro in enumerate(coros):
            task = asyncio.create_task(coro)
            task_info = TaskInfo(
                task=task,
                name=f"concurrent_{i}",
                started_at=time.perf_counter(),
                timeout=timeout
            )
            tasks.append(task)
            
            async with self._lock:
                self._tasks[f"concurrent_{i}"] = task_info
                
        self._logger.debug(
            f"Running {len(tasks)} concurrent steps",
            extra={"count": len(tasks), "timeout": timeout}
        )
        
        try:
            results = await asyncio.wait_for(
                asyncio.gather(*tasks, return_exceptions=return_exceptions),
                timeout
            )
            
            self._logger.debug(
                f"Completed {len(tasks)} concurrent steps",
                extra={"count": len(tasks)}
            )
            
            return results
            
        except asyncio.TimeoutError:
            # Cancel all tasks
            for task in tasks:
                task.cancel()
                
            raise StepTimeoutException(
                f"Concurrent steps timed out after {timeout}s"
            )
            
        finally:
            # Clean up task tracking
            async with self._lock:
                for i in range(len(coros)):
                    self._tasks.pop(f"concurrent_{i}", None)
                    
    async def cleanup(self) -> None:
        """
        Cancel all pending tasks and run cleanup callbacks.
        
        Ensures all resources are properly released.
        """
        self._logger.info("Starting async cleanup")
        
        # Set cleanup event
        self._cleanup_event.set()
        
        # Cancel all pending tasks
        async with self._lock:
            tasks_to_cancel = list(self._tasks.values())
            
        for task_info in tasks_to_cancel:
            if not task_info.task.done():
                self._logger.debug(
                    f"Cancelling task: {task_info.name}",
                    extra={"task_name": task_info.name}
                )
                task_info.task.cancel()
                
        # Wait for all tasks to complete cancellation
        if tasks_to_cancel:
            await asyncio.gather(
                *[t.task for t in tasks_to_cancel],
                return_exceptions=True
            )
            
        # Run cleanup callbacks
        for callback in self._cleanup_callbacks:
            try:
                if asyncio.iscoroutinefunction(callback):
                    await callback()
                else:
                    callback()
            except Exception as e:
                self._logger.error(
                    f"Cleanup callback failed: {e}",
                    exc_info=True
                )
                
        # Clear state
        async with self._lock:
            self._tasks.clear()
            
        self._cleanup_callbacks.clear()
        self._cleanup_event.clear()
        
        self._logger.info("Async cleanup completed")
        
    def register_cleanup(self, callback: Callable[[], Any]) -> None:
        """
        Register a cleanup callback.
        
        Args:
            callback: Function to call during cleanup
        """
        self._cleanup_callbacks.append(callback)
        
    async def wait_for_cleanup(self, timeout: float = 5.0) -> None:
        """
        Wait for cleanup event to be set.
        
        Args:
            timeout: Maximum time to wait
            
        Raises:
            AsyncCleanupException: If timeout exceeded
        """
        try:
            await asyncio.wait_for(
                self._cleanup_event.wait(),
                timeout
            )
        except asyncio.TimeoutError:
            raise AsyncCleanupException(
                f"Cleanup event not set after {timeout}s"
            )
            
    @asynccontextmanager
    async def tracked_task(
        self,
        name: str,
        timeout: Optional[float] = None
    ) -> AsyncGenerator[TaskInfo, None]:
        """
        Context manager for tracking a task.
        
        Args:
            name: Task name
            timeout: Task timeout
            
        Yields:
            None
        """
        task = asyncio.current_task()
        if task is None:
            raise RuntimeError("No current task")
            
        task_info = TaskInfo(
            task=task,
            name=name,
            started_at=time.perf_counter(),
            timeout=timeout
        )
        
        async with self._lock:
            self._tasks[name] = task_info
            
        try:
            yield task_info
        finally:
            async with self._lock:
                self._tasks.pop(name, None)
                
    async def get_running_tasks(self) -> List[str]:
        """
        Get list of currently running task names.
        
        Returns:
            List of task names
        """
        async with self._lock:
            return [
                name for name, info in self._tasks.items()
                if not info.task.done()
            ]
            
    async def cancel_task(self, name: str) -> bool:
        """
        Cancel a specific task by name.
        
        Args:
            name: Task name to cancel
            
        Returns:
            True if task was cancelled, False if not found
        """
        async with self._lock:
            task_info = self._tasks.get(name)
            
        if task_info and not task_info.task.done():
            task_info.task.cancel()
            self._logger.debug(
                f"Cancelled task: {name}",
                extra={"task_name": name}
            )
            return True
            
        return False
        
    def create_timeout_handler(
        self,
        default_timeout: float
    ) -> Callable:
        """
        Create a decorator for adding timeouts to async functions.
        
        Args:
            default_timeout: Default timeout in seconds
            
        Returns:
            Decorator function
        """
        def decorator(func: Callable) -> Callable:
            @functools.wraps(func)
            async def wrapper(*args: object, **kwargs: object) -> T:
                # Allow timeout override via kwargs
                timeout_override = kwargs.pop('_timeout', None)
                if timeout_override is not None:
                    if isinstance(timeout_override, (int, float)):
                        timeout = float(timeout_override)
                    else:
                        timeout = default_timeout
                else:
                    timeout = default_timeout
                
                return await self.run_step_with_timeout(
                    func(*args, **kwargs),
                    timeout=timeout,
                    step_name=func.__name__
                )
                
            return wrapper
        return decorator


class AsyncResourceManager:
    """
    Manage async resources with automatic cleanup.
    
    Ensures resources like connections, buffers, etc.
    are properly released even on failure.
    """
    
    def __init__(self, logger: logging.Logger) -> None:
        """
        Initialize AsyncResourceManager.
        
        Args:
            logger: Logger for operations
        """
        self._logger = logger
        self._resources: Dict[str, Any] = {}
        self._cleanup_funcs: Dict[str, Callable] = {}
        self._lock = Lock()
        
    async def register(
        self,
        name: str,
        resource: Any,
        cleanup: Optional[Callable] = None
    ) -> None:
        """
        Register a resource for management.
        
        Args:
            name: Resource name
            resource: Resource object
            cleanup: Optional cleanup function
        """
        async with self._lock:
            self._resources[name] = resource
            if cleanup:
                self._cleanup_funcs[name] = cleanup
                
        self._logger.debug(
            f"Registered resource: {name}",
            extra={"resource_name": name}
        )
        
    async def get(self, name: str) -> Any:
        """
        Get a registered resource.
        
        Args:
            name: Resource name
            
        Returns:
            Resource object
            
        Raises:
            KeyError: If resource not found
        """
        async with self._lock:
            return self._resources[name]
            
    async def release(self, name: str) -> None:
        """
        Release a specific resource.
        
        Args:
            name: Resource name to release
        """
        async with self._lock:
            resource = self._resources.pop(name, None)
            cleanup = self._cleanup_funcs.pop(name, None)
            
        if resource is not None:
            if cleanup:
                try:
                    if asyncio.iscoroutinefunction(cleanup):
                        await cleanup(resource)
                    else:
                        cleanup(resource)
                except Exception as e:
                    self._logger.error(
                        f"Failed to cleanup resource {name}: {e}",
                        extra={"resource_name": name},
                        exc_info=True
                    )
                    
            self._logger.debug(
                f"Released resource: {name}",
                extra={"resource_name": name}
            )
            
    async def cleanup_all(self) -> None:
        """Release all managed resources."""
        async with self._lock:
            names = list(self._resources.keys())
            
        for name in names:
            await self.release(name)
            
        self._logger.info("All async resources cleaned up")
        
    @asynccontextmanager
    async def managed_resource(
        self,
        name: str,
        factory: Callable,
        cleanup: Optional[Callable] = None
    ) -> AsyncGenerator[object, None]:
        """
        Context manager for automatic resource management.
        
        Args:
            name: Resource name
            factory: Function to create resource
            cleanup: Optional cleanup function
            
        Yields:
            Created resource
        """
        resource = None
        try:
            if asyncio.iscoroutinefunction(factory):
                resource = await factory()
            else:
                resource = factory()
                
            await self.register(name, resource, cleanup)
            yield resource
            
        finally:
            await self.release(name)