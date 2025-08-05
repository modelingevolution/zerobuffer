#!/usr/bin/env python3
"""
Run a Harmony test scenario directly using Python serve
"""

import json
import subprocess
import asyncio
import sys
import os
import time


class HarmonyTestRunner:
    def __init__(self):
        self.reader_proc = None
        self.writer_proc = None
        
    def start_serve(self, role):
        """Start a Python serve process with the given role"""
        proc = subprocess.Popen(
            ['./venv/bin/python', 'zerobuffer_serve.py'],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            bufsize=0
        )
        return proc
        
    def send_request(self, proc, request):
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
        
    def execute_step(self, proc, process_name, step_type, step_text, is_broadcast=False):
        """Execute a test step"""
        response = self.send_request(proc, {
            'jsonrpc': '2.0',
            'method': 'executeStep',
            'params': {
                'process': process_name,
                'stepType': step_type,
                'step': step_text,
                'originalStep': f"{step_type.title()} {step_text}",
                'parameters': {},
                'isBroadcast': is_broadcast
            },
            'id': 'step'
        })
        
        result = response.get('result', {})
        if not result.get('success', False):
            error = result.get('error', 'Unknown error')
            print(f"  ✗ Step failed: {error}")
            # Print logs for debugging
            for log in result.get('logs', []):
                print(f"    [{log['level']}] {log['message']}")
            raise RuntimeError(f"Step failed: {error}")
        else:
            print(f"  ✓ {step_type.title()}: {step_text}")
            
    def run_test_1_1(self):
        """Run Test 1.1 - Simple Write-Read Cycle"""
        print("\n=== Test 1.1 - Simple Write-Read Cycle [Python/Python] ===\n")
        
        try:
            # Start reader and writer processes
            print("Starting reader process...")
            self.reader_proc = self.start_serve('reader')
            time.sleep(1)
            
            print("Starting writer process...")
            self.writer_proc = self.start_serve('writer')
            time.sleep(1)
            
            # Initialize both processes
            print("\nInitializing processes...")
            self.send_request(self.reader_proc, {
                'jsonrpc': '2.0',
                'method': 'initialize',
                'params': {
                    'hostPid': os.getpid(),
                    'featureId': 1,
                    'role': 'reader',
                    'platform': 'python',
                    'scenario': 'Test 1.1 - Simple Write-Read Cycle',
                    'testRunId': 'test-1-1'
                },
                'id': 1
            })
            
            self.send_request(self.writer_proc, {
                'jsonrpc': '2.0',
                'method': 'initialize',
                'params': {
                    'hostPid': os.getpid(),
                    'featureId': 1,
                    'role': 'writer',
                    'platform': 'python',
                    'scenario': 'Test 1.1 - Simple Write-Read Cycle',
                    'testRunId': 'test-1-1'
                },
                'id': 1
            })
            
            print("✓ Both processes initialized\n")
            
            # Execute test steps
            print("Executing test steps:")
            
            # Background steps
            self.execute_step(self.reader_proc, 'reader', 'given', 'the test environment is initialized')
            self.execute_step(self.reader_proc, 'reader', 'given', 'all processes are ready')
            
            # Test steps
            self.execute_step(
                self.reader_proc, 'reader', 'given',
                "the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'"
            )
            
            self.execute_step(
                self.writer_proc, 'writer', 'when',
                "the 'writer' process connects to buffer 'test-basic'"
            )
            
            self.execute_step(
                self.writer_proc, 'writer', 'when',
                "the 'writer' process writes metadata with size '100'"
            )
            
            self.execute_step(
                self.writer_proc, 'writer', 'when',
                "the 'writer' process writes frame with size '1024' and sequence '1'"
            )
            
            self.execute_step(
                self.reader_proc, 'reader', 'then',
                "the 'reader' process should read frame with sequence '1' and size '1024'"
            )
            
            self.execute_step(
                self.reader_proc, 'reader', 'then',
                "the 'reader' process should validate frame data"
            )
            
            self.execute_step(
                self.reader_proc, 'reader', 'then',
                "the 'reader' process signals space available"
            )
            
            print("\n✅ Test 1.1 completed successfully!\n")
            
            # Cleanup
            print("Cleaning up...")
            self.send_request(self.reader_proc, {
                'jsonrpc': '2.0',
                'method': 'cleanup',
                'params': {},
                'id': 'cleanup'
            })
            self.send_request(self.writer_proc, {
                'jsonrpc': '2.0',
                'method': 'cleanup',
                'params': {},
                'id': 'cleanup'
            })
            
        except Exception as e:
            print(f"\n❌ Test failed: {e}")
            raise
        finally:
            # Terminate processes
            if self.reader_proc:
                self.reader_proc.terminate()
                self.reader_proc.wait(timeout=2)
            if self.writer_proc:
                self.writer_proc.terminate()
                self.writer_proc.wait(timeout=2)


def main():
    runner = HarmonyTestRunner()
    runner.run_test_1_1()


if __name__ == '__main__':
    main()