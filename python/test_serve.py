#!/usr/bin/env python3
"""
Test script for ZeroBuffer Python Serve

Tests basic JSON-RPC communication with the serve application.
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
    """Test the serve application"""
    print("Starting ZeroBuffer Python Serve test...")
    
    # Start the serve process
    proc = subprocess.Popen(
        [sys.executable, 'zerobuffer_serve.py'],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        bufsize=0
    )
    
    try:
        # Give it time to start
        time.sleep(1)
        
        # Test health check
        print("\n1. Testing health check...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'health',
            'params': {'hostPid': 12345, 'featureId': 67},
            'id': 1
        })
        print(f"Response: {response}")
        assert response['result'] == True
        
        # Test initialize
        print("\n2. Testing initialize...")
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
            'id': 2
        })
        print(f"Response: {response}")
        assert response['result'] == True
        
        # Test discover
        print("\n3. Testing discover...")
        response = send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'discover',
            'params': {},
            'id': 3
        })
        print(f"Response: {response}")
        steps = response['result']['steps']
        print(f"Discovered {len(steps)} step definitions")
        
        # Test shutdown
        print("\n4. Testing shutdown...")
        # Send shutdown notification with proper header
        shutdown_request = json.dumps({
            'jsonrpc': '2.0',
            'method': 'shutdown',
            'params': {}
        })
        content = shutdown_request.encode('utf-8')
        header = f"Content-Length: {len(content)}\r\n\r\n".encode('utf-8')
        proc.stdin.write(header)
        proc.stdin.write(content)
        proc.stdin.flush()
        
        # Give it a moment to process
        time.sleep(0.5)
        
        # Check if process exited
        if proc.poll() is None:
            print("Process did not exit gracefully, terminating...")
            proc.terminate()
            
        # Wait for process to exit
        proc.wait(timeout=2)
        
        print("\nAll tests passed!")
        
    except Exception as e:
        print(f"\nTest failed: {e}")
        proc.terminate()
        raise
    finally:
        # Make sure process is terminated
        if proc.poll() is None:
            proc.terminate()


if __name__ == '__main__':
    main()