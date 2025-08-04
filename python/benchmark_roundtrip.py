#!/usr/bin/env python3
"""
ZeroBuffer Cross-Process Round-Trip Latency Benchmark

Measures round-trip latency for video frames through a relay process.
Similar to the C# benchmark implementation.
"""

import os
import sys
import time
import struct
import random
import asyncio
import statistics
import subprocess
from typing import List, Tuple
from datetime import datetime

from zerobuffer import Reader, Writer, BufferConfig
from zerobuffer.exceptions import ZeroBufferException


# Frame structure constants
HEADER_SIZE = 16  # 8 bytes timestamp + 4 bytes frame_id + 4 bytes padding
FULLHD_1080P_SIZE = 1920 * 1080 * 3 // 2  # YUV420: 3,110,400 bytes
TOTAL_FRAME_SIZE = HEADER_SIZE + FULLHD_1080P_SIZE

# Test parameters
WARMUP_FRAMES = 100
TEST_FRAMES_PER_FPS = 1000


def get_timestamp_ns() -> int:
    """Get high-resolution timestamp in nanoseconds"""
    return time.perf_counter_ns()


def pack_frame_header(timestamp: int, frame_id: int) -> bytes:
    """Pack timestamp and frame ID into header"""
    # Q = unsigned long long (8 bytes), I = unsigned int (4 bytes)
    return struct.pack('<QII', timestamp, frame_id, 0)  # 0 is padding


def unpack_frame_header(data: bytes) -> Tuple[int, int]:
    """Unpack timestamp and frame ID from header"""
    timestamp, frame_id, _ = struct.unpack('<QII', data[:HEADER_SIZE])
    return timestamp, frame_id


async def start_relay_process(input_buffer: str, output_buffer: str) -> subprocess.Popen:
    """Start the relay helper process"""
    relay_script = os.path.join(os.path.dirname(__file__), 'zerobuffer_relay.py')
    
    cmd = [sys.executable, relay_script, input_buffer, output_buffer]
    
    process = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )
    
    # Wait for relay to initialize
    await asyncio.sleep(1.0)
    
    if process.poll() is not None:
        stderr = process.stderr.read() if process.stderr else ""
        raise RuntimeError(f"Relay process failed to start: {stderr}")
    
    return process


async def measure_latency(target_fps: int) -> None:
    """Measure round-trip latency at specified FPS"""
    test_id = f"{os.getpid()}_{int(time.time())}"
    buffer_to_relay = f"bench_to_relay_{test_id}"
    buffer_from_relay = f"bench_from_relay_{test_id}"
    
    # Start relay process
    relay_process = await start_relay_process(buffer_to_relay, buffer_from_relay)
    
    try:
        await run_benchmark(buffer_to_relay, buffer_from_relay, target_fps)
    finally:
        # Stop relay process
        relay_process.terminate()
        try:
            relay_process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            relay_process.kill()
            relay_process.wait()


async def run_benchmark(buffer_to_relay: str, buffer_from_relay: str, target_fps: int) -> None:
    """Run the actual benchmark"""
    config = BufferConfig(metadata_size=4096, payload_size=256 * 1024 * 1024)  # 256MB
    latencies = []
    
    # Prepare frame data
    frame_data = bytearray(TOTAL_FRAME_SIZE)
    # Random video data after header
    frame_data[HEADER_SIZE:] = random.randbytes(FULLHD_1080P_SIZE)
    
    # Create our output buffer as reader (relay will connect as writer)
    reader_from_relay = Reader(buffer_from_relay, config)
    
    # Give relay time to connect to our output buffer
    await asyncio.sleep(0.5)
    
    # Connect to relay's input buffer as writer
    writer_to_relay = None
    for retry in range(20):  # 2 second timeout
        try:
            writer_to_relay = Writer(buffer_to_relay)
            break
        except ZeroBufferException:
            await asyncio.sleep(0.1)
    
    if writer_to_relay is None:
        raise RuntimeError(f"Failed to connect to relay input buffer: {buffer_to_relay}")
    
    try:
        frame_interval = 1.0 / target_fps
        
        # Warmup
        print("  Warming up...", end='', flush=True)
        for i in range(WARMUP_FRAMES):
            # Pack header
            timestamp = get_timestamp_ns()
            frame_data[:HEADER_SIZE] = pack_frame_header(timestamp, i)
            
            writer_to_relay.write_frame(bytes(frame_data))
            
            # Try to read response
            response = reader_from_relay.read_frame(timeout=0.01)
            if response:
                reader_from_relay.release_frame(response)
            
            await asyncio.sleep(frame_interval)
        
        print(" done")
        
        # Clear any pending frames
        while True:
            frame = reader_from_relay.read_frame(timeout=0)
            if not frame:
                break
            reader_from_relay.release_frame(frame)
        
        # Main test
        print(f"  Measuring {TEST_FRAMES_PER_FPS} frames...", end='', flush=True)
        frames_sent = 0
        frames_received = 0
        
        # Send frames at target FPS
        start_time = time.perf_counter()
        next_frame_time = start_time
        
        for i in range(TEST_FRAMES_PER_FPS):
            # Send frame with timestamp
            send_timestamp = get_timestamp_ns()
            frame_data[:HEADER_SIZE] = pack_frame_header(send_timestamp, i)
            writer_to_relay.write_frame(bytes(frame_data))
            frames_sent += 1
            
            # Try to read response immediately
            response = reader_from_relay.read_frame(timeout=0.05)
            if response and len(response.data) >= HEADER_SIZE:
                receive_timestamp = get_timestamp_ns()
                
                # Extract original timestamp
                original_timestamp, frame_id = unpack_frame_header(bytes(response.data))
                
                # Calculate round-trip latency in microseconds
                latency_ns = receive_timestamp - original_timestamp
                latency_us = latency_ns / 1000.0
                latencies.append(latency_us)
                frames_received += 1
                
                reader_from_relay.release_frame(response)
            
            # Wait for next frame time
            next_frame_time += frame_interval
            sleep_time = next_frame_time - time.perf_counter()
            if sleep_time > 0:
                await asyncio.sleep(sleep_time)
        
        # Read any remaining responses
        await asyncio.sleep(0.1)
        while True:
            response = reader_from_relay.read_frame(timeout=0)
            if not response:
                break
            
            if len(response.data) >= HEADER_SIZE:
                receive_timestamp = get_timestamp_ns()
                original_timestamp, frame_id = unpack_frame_header(bytes(response.data))
                latency_ns = receive_timestamp - original_timestamp
                latency_us = latency_ns / 1000.0
                latencies.append(latency_us)
                frames_received += 1
            
            reader_from_relay.release_frame(response)
        
        print(" done")
        
        # Print results
        print(f"  Frames sent: {frames_sent}, received: {frames_received}")
        
        if latencies:
            latencies.sort()
            min_lat = min(latencies)
            max_lat = max(latencies)
            avg_lat = statistics.mean(latencies)
            p50_lat = statistics.quantiles(latencies, n=100)[49]  # 50th percentile
            p90_lat = statistics.quantiles(latencies, n=100)[89]  # 90th percentile
            p99_lat = statistics.quantiles(latencies, n=100)[98]  # 99th percentile
            
            print("  Round-trip latency (microseconds):")
            print(f"    Min:  {min_lat:8.0f} μs")
            print(f"    Avg:  {avg_lat:8.0f} μs")
            print(f"    P50:  {p50_lat:8.0f} μs")
            print(f"    P90:  {p90_lat:8.0f} μs")
            print(f"    P99:  {p99_lat:8.0f} μs")
            print(f"    Max:  {max_lat:8.0f} μs")
        else:
            print("  ERROR: No latency measurements collected!")
            
    finally:
        writer_to_relay.close()
        reader_from_relay.close()


async def run_all_tests() -> None:
    """Run tests at various FPS levels"""
    print("ZeroBuffer Cross-Process Round-Trip Latency Benchmark")
    print("=====================================================")
    print()
    print(f"Frame size: YUV420 Full HD + timestamp header ({TOTAL_FRAME_SIZE:,} bytes)")
    print(f"Warmup frames: {WARMUP_FRAMES}")
    print(f"Test frames per FPS: {TEST_FRAMES_PER_FPS}")
    print()
    
    # Test at various FPS levels
    fps_targets = [30, 60, 120, 240, 500, 1000]
    
    for fps in fps_targets:
        print(f"--- Testing at {fps} FPS ---")
        await measure_latency(fps)
        print()
        
        # Small delay between tests
        await asyncio.sleep(1.0)


async def main():
    """Main entry point"""
    try:
        await run_all_tests()
    except KeyboardInterrupt:
        print("\nBenchmark interrupted by user")
    except Exception as e:
        print(f"\nError: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    asyncio.run(main())