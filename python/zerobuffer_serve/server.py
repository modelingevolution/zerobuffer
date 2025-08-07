"""
JSON-RPC server implementation for ZeroBuffer serve

Uses python-lsp-jsonrpc for protocol handling over stdin/stdout.
"""

import asyncio
import json
import logging
import sys
from typing import Dict, Any, Callable, Optional, BinaryIO, Union, List
from io import BufferedReader, BufferedWriter

from .models import (
    HealthRequest,
    InitializeRequest,
    StepRequest,
    StepResponse,
    DiscoverResponse,
    LogEntry
)
from .step_registry import StepRegistry
from .test_context import TestContext
from .logging.dual_logger import DualLoggerProvider


class ZeroBufferServe:
    """JSON-RPC server for ZeroBuffer test execution"""
    
    def __init__(
        self,
        step_registry: StepRegistry,
        test_context: TestContext,
        logger_provider: DualLoggerProvider
    ):
        self._step_registry = step_registry
        self._test_context = test_context
        self._logger_provider = logger_provider
        self._logger = logger_provider.get_logger(self.__class__.__name__)
        self._running = False
        
    async def run(self) -> None:
        """Run the JSON-RPC server on stdin/stdout"""
        self._logger.info("Starting JSON-RPC server on stdin/stdout")
        self._running = True
        
        # Set up stdin/stdout for binary mode
        stdin = sys.stdin.buffer
        stdout = sys.stdout.buffer
        
        # Create tasks for reading and writing
        read_task = asyncio.create_task(self._read_loop(stdin))
        
        try:
            await read_task
        except asyncio.CancelledError:
            self._logger.info("Server cancelled")
        except Exception as e:
            self._logger.error(f"Server error: {e}", exc_info=True)
        finally:
            self._running = False
            self._logger.info("JSON-RPC server stopped")
    
    async def _read_loop(self, stdin: BinaryIO) -> None:
        """Read and process JSON-RPC requests from stdin"""
        loop = asyncio.get_event_loop()
        
        while self._running:
            try:
                # Read headers first (LSP-style protocol)
                headers = {}
                while True:
                    header_line = await loop.run_in_executor(None, stdin.readline)
                    if not header_line:
                        # End of stream
                        return
                    
                    header_str = header_line.decode('utf-8').strip()
                    if not header_str:
                        # Empty line marks end of headers
                        break
                        
                    # Parse header (e.g., "Content-Length: 123")
                    if ':' in header_str:
                        key, value = header_str.split(':', 1)
                        headers[key.strip()] = value.strip()
                
                # Get content length from headers
                if 'Content-Length' not in headers:
                    self._logger.error("Missing Content-Length header")
                    continue
                    
                content_length = int(headers['Content-Length'])
                
                # Read the JSON content
                content_bytes = await loop.run_in_executor(None, stdin.read, content_length)
                if not content_bytes:
                    break
                    
                request_text = content_bytes.decode('utf-8')
                
                # Parse and handle request
                # self._logger.debug(f"Received request: {request_text}")  # Too verbose
                
                response = await self._handle_request(request_text)
                
                if response:
                    await self._send_response(response)
                    
            except Exception as e:
                self._logger.error(f"Error in read loop: {e}", exc_info=True)
                
    async def _handle_request(self, request_text: str) -> Optional[str]:
        """Handle a JSON-RPC request and return response"""
        try:
            request = json.loads(request_text)
            
            # Extract request details
            method = request.get('method', '')
            params = request.get('params', {})
            request_id = request.get('id')
            
            # Normalize params - some clients send arrays, others send objects
            if params is None:
                params = {}
            
            # Route to appropriate handler
            result = await self._route_method(method, params)
            
            # Build response
            if request_id is not None:
                response = {
                    'jsonrpc': '2.0',
                    'id': request_id,
                    'result': result
                }
                return json.dumps(response)
            
            # No response for notifications (no id)
            return None
            
        except Exception as e:
            self._logger.error(f"Error handling request: {e}", exc_info=True)
            
            # Return error response if we have an id
            if 'request' in locals() and request.get('id') is not None:
                error_response = {
                    'jsonrpc': '2.0',
                    'id': request.get('id'),
                    'error': {
                        'code': -32603,
                        'message': str(e)
                    }
                }
                return json.dumps(error_response)
                
            return None
    
    async def _route_method(self, method: str, params: Union[Dict[str, Any], List[Any], None]) -> Any:
        """Route method to appropriate handler"""
        handlers = {
            'health': self._handle_health,
            'initialize': self._handle_initialize,
            'discover': self._handle_discover,
            'executeStep': self._handle_execute_step,
            'cleanup': self._handle_cleanup,
            'shutdown': self._handle_shutdown
        }
        
        handler = handlers.get(method)
        if not handler:
            raise ValueError(f"Unknown method: {method}")
            
        # Ensure params is not None for handlers
        if params is None:
            params = {}
            
        return await handler(params)
    
    async def _send_response(self, response: str) -> None:
        """Send JSON-RPC response to stdout with LSP-style headers"""
        # Encode response to bytes to get accurate length
        response_bytes = response.encode('utf-8')
        content_length = len(response_bytes)
        
        # Send headers (Content-Length is required)
        sys.stdout.buffer.write(f"Content-Length: {content_length}\r\n".encode('utf-8'))
        sys.stdout.buffer.write(b"\r\n")  # Empty line to end headers
        
        # Send the JSON content
        sys.stdout.buffer.write(response_bytes)
        sys.stdout.buffer.flush()
        
        # self._logger.debug(f"Sent response with Content-Length: {content_length}: {response}")  # Too verbose
    
    async def _handle_health(self, params: Union[Dict[str, Any], List[Any]]) -> bool:
        """Handle health check request"""
        # Handle various parameter formats from different clients
        if isinstance(params, list):
            if len(params) == 1 and isinstance(params[0], dict):
                # Harmony format: [{'hostPid': 123, 'featureId': 1}]
                request = HealthRequest(**params[0])
            elif len(params) >= 2:
                # Pure positional parameters [hostPid, featureId]
                request = HealthRequest(hostPid=params[0], featureId=params[1])
            else:
                # Default values for minimal health check
                request = HealthRequest(hostPid=0, featureId=0)
        else:
            # Direct named parameters (must be dict if not list)
            request = HealthRequest(**params)
            
        self._logger.info(f"Health check requested with hostPid: {request.hostPid}, featureId: {request.featureId}")
        return True
    
    async def _handle_initialize(self, params: Union[Dict[str, Any], List[Any]]) -> bool:
        """Handle initialization request"""
        # Handle various parameter formats from different clients
        if isinstance(params, list):
            if len(params) == 1 and isinstance(params[0], dict):
                # Harmony format: [{'hostPid': 123, 'featureId': 1, ...}]
                param_dict = params[0]
                request = InitializeRequest(**param_dict)
            elif len(params) >= 6:
                # Pure positional parameters [hostPid, featureId, role, platform, scenario, testRunId]
                request = InitializeRequest(
                    hostPid=params[0],
                    featureId=params[1], 
                    role=params[2],
                    platform=params[3],
                    scenario=params[4],
                    testRunId=params[5] if len(params) > 5 else ""
                )
            else:
                raise ValueError(f"Invalid initialize parameters: {params}")
        elif isinstance(params, dict):
            # Direct named parameters
            request = InitializeRequest(**params)
        else:
            raise ValueError(f"Invalid initialize parameters: {params}")
            
        self._logger.info(
            f"Initializing with hostPid: {request.hostPid}, featureId: {request.featureId}, "
            f"role: {request.role}, platform: {request.platform}, scenario: {request.scenario}"
        )
        
        try:
            # Initialize test context
            self._test_context.initialize(
                role=request.role,
                platform=request.platform,
                scenario=request.scenario,
                test_run_id=request.testRunId
            )
            
            # Store Harmony process management parameters
            self._test_context.set_data("harmony_host_pid", request.hostPid)
            self._test_context.set_data("harmony_feature_id", request.featureId)
            
            self._logger.info("Initialization successful")
            return True
            
        except Exception as e:
            self._logger.error(f"Failed to initialize: {e}", exc_info=True)
            return False
    
    async def _handle_discover(self, params: Union[Dict[str, Any], List[Any], None]) -> Dict[str, Any]:
        """Handle step discovery request"""
        self._logger.info("Discovering available step definitions")
        
        steps = self._step_registry.get_all_steps()
        response = DiscoverResponse(steps=steps)
        
        self._logger.info(f"Discovered {len(response.steps)} step definitions")
        
        # Convert to dict for JSON serialization
        return {
            'steps': [
                {'type': step.type, 'pattern': step.pattern}
                for step in response.steps
            ]
        }
    
    async def _handle_execute_step(self, params: Union[Dict[str, Any], List[Any]]) -> Dict[str, Any]:
        """Handle step execution request"""
        # Handle various parameter formats from different clients
        if isinstance(params, list):
            if len(params) == 1 and isinstance(params[0], dict):
                # Harmony format: [{'stepType': 'Given', 'step': '...', ...}]
                request = StepRequest(**params[0])
            elif len(params) >= 4:
                # Pure positional parameters [stepType, step, parameters, table]
                request = StepRequest(
                    stepType=params[0],
                    step=params[1],
                    parameters=params[2] if len(params) > 2 else {},
                    table=params[3] if len(params) > 3 else None
                )
            else:
                raise ValueError(f"Invalid executeStep parameters: {params}")
        elif isinstance(params, dict):
            # Direct named parameters - handle different parameter names
            # Map common variations to expected names
            if 'type' in params and 'stepType' not in params:
                params['stepType'] = params.pop('type')
            if 'text' in params and 'step' not in params:
                params['step'] = params.pop('text')
            request = StepRequest(**params)
        else:
            raise ValueError(f"Invalid executeStep parameters: {params}")
            
        self._logger.info(f"Executing step: {request.stepType} {request.step}")
        
        try:
            # Execute the step
            result = await self._step_registry.execute_step(
                step_type=request.stepType,
                step_text=request.step,
                parameters=request.parameters,
                table=request.table
            )
            
            self._logger.info("Step executed successfully")
            
            # Collect logs
            logs = self._logger_provider.get_all_logs()
            result.logs.extend(logs)
            
            # Convert to dict for JSON serialization
            return {
                'success': result.success,
                'error': result.error,
                'data': result.data,
                'logs': [
                    {'level': log.level, 'message': log.message}
                    for log in result.logs
                ]
            }
            
        except Exception as e:
            self._logger.error(f"Step execution failed: {e}", exc_info=True)
            
            # Get all logs including the error
            logs = self._logger_provider.get_all_logs()
            
            return {
                'success': False,
                'error': str(e),
                'data': {},
                'logs': [
                    {'level': log.level, 'message': log.message}
                    for log in logs
                ]
            }
    
    async def _handle_cleanup(self, params: Union[Dict[str, Any], List[Any], None]) -> None:
        """Handle cleanup request"""
        self._logger.info("Cleaning up resources")
        
        try:
            self._test_context.cleanup()
        except Exception as e:
            self._logger.error(f"Cleanup failed: {e}", exc_info=True)
            raise
    
    async def _handle_shutdown(self, params: Union[Dict[str, Any], List[Any], None]) -> None:
        """Handle shutdown request"""
        self._logger.info("Shutdown requested")
        self._running = False