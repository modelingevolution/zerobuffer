"""
Resource monitoring for ZeroBuffer tests.

Provides system resource tracking and leak detection during test execution.
"""

import os
import time
import psutil
import resource
import logging
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Any
from contextlib import contextmanager
from threading import Lock


@dataclass
class ResourceSnapshot:
    """System resource snapshot at a point in time."""
    timestamp: float
    memory_mb: float
    cpu_percent: float
    open_files: int
    threads: int
    shared_memory_segments: int
    semaphores: int
    
    def __str__(self) -> str:
        """Human-readable representation."""
        return (
            f"ResourceSnapshot(memory={self.memory_mb:.1f}MB, "
            f"cpu={self.cpu_percent:.1f}%, files={self.open_files}, "
            f"threads={self.threads}, shm={self.shared_memory_segments}, "
            f"sem={self.semaphores})"
        )


@dataclass
class ResourceDelta:
    """Resource usage delta between two snapshots."""
    duration_seconds: float
    memory_delta_mb: float
    files_delta: int
    threads_delta: int
    shm_delta: int
    sem_delta: int
    
    def has_leak(self, threshold_mb: float = 10) -> bool:
        """Check if delta indicates a resource leak."""
        return (
            self.memory_delta_mb > threshold_mb or
            self.files_delta > 5 or
            self.shm_delta > 0 or
            self.sem_delta > 0
        )


class ResourceMonitor:
    """
    Monitor system resource usage during tests.
    
    Tracks memory, CPU, file descriptors, and IPC resources
    to detect leaks and performance issues.
    """
    
    def __init__(
        self,
        logger: logging.Logger,
        warn_memory_mb: float = 500,
        warn_cpu_percent: float = 80,
        enable_tracking: bool = True
    ) -> None:
        """
        Initialize ResourceMonitor.
        
        Args:
            logger: Logger for warnings and info
            warn_memory_mb: Memory threshold for warnings
            warn_cpu_percent: CPU threshold for warnings
            enable_tracking: Whether to enable resource tracking
        """
        self._logger = logger
        self._warn_memory_mb = warn_memory_mb
        self._warn_cpu_percent = warn_cpu_percent
        self._enabled = enable_tracking
        
        self._process = psutil.Process()
        self._initial_snapshot: Optional[ResourceSnapshot] = None
        self._snapshots: List[ResourceSnapshot] = []
        self._lock = Lock()
        
        if self._enabled:
            self._initial_snapshot = self.capture()
            
    def capture(self) -> ResourceSnapshot:
        """
        Capture current resource usage.
        
        Returns:
            ResourceSnapshot with current metrics
        """
        if not self._enabled:
            return ResourceSnapshot(
                timestamp=time.time(),
                memory_mb=0,
                cpu_percent=0,
                open_files=0,
                threads=0,
                shared_memory_segments=0,
                semaphores=0
            )
            
        try:
            # Memory and CPU
            memory_info = self._process.memory_info()
            memory_mb = memory_info.rss / 1024 / 1024
            cpu_percent = self._process.cpu_percent()
            
            # File descriptors
            try:
                open_files = len(self._process.open_files())
            except (psutil.AccessDenied, psutil.NoSuchProcess):
                open_files = 0
                
            # Threads
            threads = self._process.num_threads()
            
            # Shared memory and semaphores (Linux-specific)
            shm_count = self._count_shared_memory()
            sem_count = self._count_semaphores()
            
            snapshot = ResourceSnapshot(
                timestamp=time.time(),
                memory_mb=memory_mb,
                cpu_percent=cpu_percent,
                open_files=open_files,
                threads=threads,
                shared_memory_segments=shm_count,
                semaphores=sem_count
            )
            
            with self._lock:
                self._snapshots.append(snapshot)
                
            return snapshot
            
        except Exception as e:
            self._logger.debug(f"Resource capture error: {e}")
            return ResourceSnapshot(
                timestamp=time.time(),
                memory_mb=0,
                cpu_percent=0,
                open_files=0,
                threads=0,
                shared_memory_segments=0,
                semaphores=0
            )
            
    def _count_shared_memory(self) -> int:
        """Count shared memory segments owned by process."""
        try:
            # Platform-specific implementation
            if os.name == 'posix':
                # Try to count /dev/shm entries
                shm_path = '/dev/shm'
                if os.path.exists(shm_path):
                    pid = self._process.pid
                    count = 0
                    for entry in os.listdir(shm_path):
                        # Check if entry name contains our PID
                        if str(pid) in entry or 'zerobuffer' in entry.lower():
                            count += 1
                    return count
        except Exception:
            pass
        return 0
        
    def _count_semaphores(self) -> int:
        """Count semaphores owned by process."""
        try:
            # Platform-specific implementation
            if os.name == 'posix':
                # Try to count semaphores via /dev/shm
                shm_path = '/dev/shm'
                if os.path.exists(shm_path):
                    count = 0
                    for entry in os.listdir(shm_path):
                        if entry.startswith('sem.'):
                            count += 1
                    return count
        except Exception:
            pass
        return 0
        
    def check_resources(self) -> Optional[ResourceDelta]:
        """
        Check for resource leaks or excessive usage.
        
        Returns:
            ResourceDelta if initial snapshot exists, None otherwise
        """
        if not self._enabled or not self._initial_snapshot:
            return None
            
        current = self.capture()
        
        # Check absolute thresholds
        if current.memory_mb > self._warn_memory_mb:
            self._logger.warning(
                f"High memory usage: {current.memory_mb:.1f}MB "
                f"(threshold: {self._warn_memory_mb}MB)",
                extra={
                    "resource_check": "memory",
                    "value": current.memory_mb,
                    "threshold": self._warn_memory_mb
                }
            )
            
        if current.cpu_percent > self._warn_cpu_percent:
            self._logger.warning(
                f"High CPU usage: {current.cpu_percent:.1f}% "
                f"(threshold: {self._warn_cpu_percent}%)",
                extra={
                    "resource_check": "cpu",
                    "value": current.cpu_percent,
                    "threshold": self._warn_cpu_percent
                }
            )
            
        # Calculate delta from initial
        delta = self.calculate_delta(self._initial_snapshot, current)
        
        # Check for leaks
        if delta.has_leak():
            self._logger.warning(
                f"Possible resource leak detected: {delta}",
                extra={
                    "resource_leak": True,
                    "memory_delta": delta.memory_delta_mb,
                    "files_delta": delta.files_delta,
                    "shm_delta": delta.shm_delta,
                    "sem_delta": delta.sem_delta
                }
            )
            
        return delta
        
    def calculate_delta(
        self,
        start: ResourceSnapshot,
        end: ResourceSnapshot
    ) -> ResourceDelta:
        """
        Calculate resource usage delta between snapshots.
        
        Args:
            start: Starting snapshot
            end: Ending snapshot
            
        Returns:
            ResourceDelta with differences
        """
        return ResourceDelta(
            duration_seconds=end.timestamp - start.timestamp,
            memory_delta_mb=end.memory_mb - start.memory_mb,
            files_delta=end.open_files - start.open_files,
            threads_delta=end.threads - start.threads,
            shm_delta=end.shared_memory_segments - start.shared_memory_segments,
            sem_delta=end.semaphores - start.semaphores
        )
        
    @contextmanager
    def track_operation(self, operation_name: str) -> Any:
        """
        Context manager to track resources during an operation.
        
        Args:
            operation_name: Name of the operation being tracked
            
        Yields:
            ResourceSnapshot at start of operation
        """
        if not self._enabled:
            yield None
            return
            
        start_snapshot = self.capture()
        
        self._logger.debug(
            f"Starting resource tracking for {operation_name}",
            extra={
                "operation": operation_name,
                "start_resources": str(start_snapshot)
            }
        )
        
        try:
            yield start_snapshot
        finally:
            end_snapshot = self.capture()
            delta = self.calculate_delta(start_snapshot, end_snapshot)
            
            self._logger.info(
                f"Resource usage for {operation_name}",
                extra={
                    "operation": operation_name,
                    "duration_seconds": delta.duration_seconds,
                    "memory_delta_mb": delta.memory_delta_mb,
                    "cpu_percent": end_snapshot.cpu_percent
                }
            )
            
            if delta.has_leak():
                self._logger.warning(
                    f"Resource leak in {operation_name}: {delta}",
                    extra={
                        "operation": operation_name,
                        "leak_detected": True,
                        "delta": str(delta)
                    }
                )
                
    def get_statistics(self) -> Dict[str, Any]:
        """
        Get resource usage statistics.
        
        Returns:
            Dictionary with statistics
        """
        if not self._enabled or not self._snapshots:
            return {}
            
        with self._lock:
            snapshots = list(self._snapshots)
            
        if len(snapshots) < 2:
            return {}
            
        memory_values = [s.memory_mb for s in snapshots]
        cpu_values = [s.cpu_percent for s in snapshots]
        
        import statistics
        
        return {
            "memory": {
                "min_mb": min(memory_values),
                "max_mb": max(memory_values),
                "mean_mb": statistics.mean(memory_values),
                "median_mb": statistics.median(memory_values)
            },
            "cpu": {
                "min_percent": min(cpu_values),
                "max_percent": max(cpu_values),
                "mean_percent": statistics.mean(cpu_values),
                "median_percent": statistics.median(cpu_values)
            },
            "snapshots": len(snapshots),
            "duration_seconds": snapshots[-1].timestamp - snapshots[0].timestamp
        }
        
    def reset(self) -> None:
        """Reset monitor to initial state."""
        with self._lock:
            self._snapshots.clear()
            if self._enabled:
                self._initial_snapshot = self.capture()
                
    def report(self) -> str:
        """
        Generate a resource usage report.
        
        Returns:
            Formatted report string
        """
        if not self._enabled:
            return "Resource monitoring disabled"
            
        stats = self.get_statistics()
        if not stats:
            return "No resource statistics available"
            
        current = self.capture()
        initial_delta = self.calculate_delta(self._initial_snapshot, current) if self._initial_snapshot else None
        
        report_lines = [
            "=== Resource Usage Report ===",
            f"Current: {current}",
        ]
        
        if initial_delta:
            report_lines.append(f"Delta from start: {initial_delta}")
            
        if stats:
            report_lines.extend([
                "",
                "Statistics:",
                f"  Memory: {stats['memory']['min_mb']:.1f}-{stats['memory']['max_mb']:.1f}MB "
                f"(avg: {stats['memory']['mean_mb']:.1f}MB)",
                f"  CPU: {stats['cpu']['min_percent']:.1f}-{stats['cpu']['max_percent']:.1f}% "
                f"(avg: {stats['cpu']['mean_percent']:.1f}%)",
                f"  Duration: {stats['duration_seconds']:.1f}s",
                f"  Samples: {stats['snapshots']}"
            ])
            
        return "\n".join(report_lines)