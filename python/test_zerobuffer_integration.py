#!/usr/bin/env python3
"""
Integration test for ZeroBuffer Python Serve

Tests that the serve application can actually create and use ZeroBuffer objects.
"""

import json
import subprocess
import time
import sys
import os


def send_request(proc, request):
    """Send a JSON-RPC request and get response"""
    request_str = json.dumps(request)
    content = request_str.encode('utf-8')
    header = f"Content-Length: {len(content)}\r\n\r\n".encode('utf-8')
    
    # Send request
    proc.stdin.write(header)
    proc.stdin.write(content)
    proc.stdin.flush()
    
    # Read response header
    header_line = proc.stdout.readline().decode('utf-8').strip()
    if not header_line.startswith('Content-Length:'):
        raise ValueError(f"Invalid header: {header_line}")
        
    content_length = int(header_line.split(':')[1].strip())
    
    # Read empty line
    proc.stdout.readline()
    
    # Read content
    content = proc.stdout.read(content_length).decode('utf-8')
    return json.loads(content)


def main():
    """Test actual ZeroBuffer creation and usage"""
    print("Starting ZeroBuffer integration test...")
    
    # Start the serve process
    proc = subprocess.Popen(
        ['./venv/bin/python', 'zerobuffer_serve.py'],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        bufsize=0
    )
    
    try:
        # Give it time to start
        time.sleep(1)
        
        # Initialize as reader
        print("\n1. Initializing as reader...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'initialize',
            'params': {
                'hostPid': os.getpid(),
                'featureId': 1,
                'role': 'reader',
                'platform': 'python',
                'scenario': 'Integration test',
                'testRunId': 'integration-test'
            },
            'id': 1
        })
        assert response['result'] == True
        print("✓ Initialized")
        
        # Create a real buffer
        print("\n2. Creating ZeroBuffer...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'executeStep',
            'params': {
                'process': 'reader',
                'stepType': 'given',
                'step': "the 'reader' process creates buffer 'integration-test' with metadata size '1024' and payload size '65536'",
                'originalStep': "Given the 'reader' process creates buffer 'integration-test' with metadata size '1024' and payload size '65536'",
                'parameters': {},
                'isBroadcast': False
            },
            'id': 2
        })
        
        print(f"Response: {response}")
        assert response['result']['success'] == True
        print("✓ Buffer created")
        
        # Check if shared memory was actually created
        shm_path = "/dev/shm/integration-test"
        if os.path.exists(shm_path):
            print(f"✓ Shared memory created: {shm_path}")
            size = os.path.getsize(shm_path)
            print(f"  Size: {size} bytes")
        else:
            print("✗ Shared memory NOT found!")
            
        # Check semaphores
        print("\n3. Checking semaphores...")
        # On Linux, semaphores might be visible in /dev/shm
        sem_files = [f for f in os.listdir("/dev/shm") if "integration-test" in f]
        if sem_files:
            print(f"✓ Found semaphore files: {sem_files}")
        else:
            print("  (Semaphores may not be visible as files)")
            
        # Clean up
        print("\n4. Cleaning up...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'cleanup',
            'params': {},
            'id': 3
        })
        print("✓ Cleanup completed")
        
        # Check if resources were cleaned
        time.sleep(0.5)
        if os.path.exists(shm_path):
            print("✗ Warning: Shared memory still exists after cleanup")
        else:
            print("✓ Shared memory cleaned up")
            
    except Exception as e:
        print(f"\nTest failed: {e}")
        # Print stderr for debugging
        stderr = proc.stderr.read().decode('utf-8')
        if stderr:
            print(f"\nStderr output:\n{stderr}")
        raise
    finally:
        # Terminate process
        proc.terminate()
        proc.wait(timeout=2)


if __name__ == '__main__':
    main()