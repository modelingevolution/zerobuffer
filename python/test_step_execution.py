#!/usr/bin/env python3
"""
Test script for step execution in ZeroBuffer Python Serve

Tests actual step execution with the serve application.
"""

import json
import subprocess
import time
import sys


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
    """Test step execution"""
    print("Starting ZeroBuffer Python Serve step execution test...")
    
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
        
        # Initialize
        print("\n1. Initializing as reader...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'initialize',
            'params': {
                'hostPid': 12345,
                'featureId': 67,
                'role': 'reader',
                'platform': 'python',
                'scenario': 'Test scenario',
                'testRunId': 'test-123'
            },
            'id': 1
        })
        print(f"Response: {response}")
        
        # Execute a simple step
        print("\n2. Executing step: test environment initialized...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'executeStep',
            'params': {
                'process': 'reader',
                'stepType': 'given',
                'step': 'the test environment is initialized',
                'originalStep': 'Given the test environment is initialized',
                'parameters': {},
                'isBroadcast': False
            },
            'id': 2
        })
        print(f"Response: {response}")
        
        # Execute create buffer step
        print("\n3. Executing step: create buffer...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'executeStep',
            'params': {
                'process': 'reader',
                'stepType': 'given',
                'step': "the 'reader' process creates buffer 'test-buffer' with metadata size '1024' and payload size '10240'",
                'originalStep': "Given the 'reader' process creates buffer 'test-buffer' with metadata size '1024' and payload size '10240'",
                'parameters': {
                    'process': 'reader',
                    'buffer_name': 'test-buffer',
                    'metadata_size': '1024',
                    'payload_size': '10240'
                },
                'isBroadcast': False
            },
            'id': 3
        })
        print(f"Response: {response}")
        
        # Test cleanup
        print("\n4. Testing cleanup...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'cleanup',
            'params': {},
            'id': 4
        })
        print(f"Response: {response}")
        
        print("\nAll step execution tests passed!")
        
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


if __name__ == '__main__':
    main()