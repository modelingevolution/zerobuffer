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
    InitializeRequest,
    StepRequest,
    StepResponse,
    DiscoverResponse,
    LogResponse
)
from datetime import datetime
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
        """Handle health check request - now parameterless per Harmony contract"""
        # Health check is now parameterless according to Harmony contract
        # We ignore any parameters sent for backward compatibility
        self._logger.info("Health check requested")
        return True
    
    async def _handle_initialize(self, params: Union[Dict[str, Any], List[Any]]) -> bool:
        """Handle initialization request"""
        # Handle various parameter formats from different clients
        if isinstance(params, list):
            if len(params) == 1 and isinstance(params[0], dict):
                # Harmony format: [{'hostPid': 123, 'featureId': 1, ...}]
                # Or C# format with PascalCase: [{'Role': 'reader', 'Platform': 'csharp', ...}]
                param_dict = params[0]
                # Normalize field names from PascalCase to camelCase
                normalized_params = {}
                for key, value in param_dict.items():
                    # Convert PascalCase to camelCase
                    normalized_key = key[0].lower() + key[1:] if key else key
                    normalized_params[normalized_key] = value
                # Remove testRunId as it's a computed property in Python
                if 'testRunId' in normalized_params:
                    normalized_params.pop('testRunId')
                request = InitializeRequest(**normalized_params)
            elif len(params) >= 6:
                # Pure positional parameters [hostPid, featureId, role, platform, scenario, testRunId]
                request = InitializeRequest(
                    hostPid=params[0],
                    featureId=params[1], 
                    role=params[2],
                    platform=params[3],
                    scenario=params[4]
                )
            else:
                raise ValueError(f"Invalid initialize parameters: {params}")
        elif isinstance(params, dict):
            # Direct named parameters - normalize field names to lowercase
            # C# sends uppercase (Role, Platform) while Python expects lowercase
            normalized_params = {}
            for key, value in params.items():
                # Convert PascalCase to lowercase
                normalized_key = key[0].lower() + key[1:] if key else key
                normalized_params[normalized_key] = value
            
            # Remove testRunId as it's a computed property in Python
            if 'testRunId' in normalized_params:
                normalized_params.pop('testRunId')
                
            request = InitializeRequest(**normalized_params)
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
                # Or C# format with PascalCase: [{'StepType': 'Given', 'Step': '...', ...}]
                param_dict = params[0]
                # Normalize field names from PascalCase to camelCase
                normalized_params = {}
                for key, value in param_dict.items():
                    # Convert PascalCase to camelCase
                    if key and key[0].isupper():
                        normalized_key = key[0].lower() + key[1:] if len(key) > 1 else key.lower()
                    else:
                        normalized_key = key
                    normalized_params[normalized_key] = value
                # Handle stepType as integer enum from C#
                if 'stepType' in normalized_params and isinstance(normalized_params['stepType'], int):
                    step_types = ['Given', 'When', 'Then']
                    normalized_params['stepType'] = step_types[normalized_params['stepType']] if normalized_params['stepType'] < len(step_types) else 'Given'
                request = StepRequest(**normalized_params)
            elif len(params) >= 3:
                # Pure positional parameters [stepType, step, parameters, context]
                request = StepRequest(
                    stepType=params[0],
                    step=params[1],
                    parameters=params[2] if len(params) > 2 else {},
                    context=params[3] if len(params) > 3 else {}
                )
            else:
                raise ValueError(f"Invalid executeStep parameters: {params}")
        elif isinstance(params, dict):
            # Direct named parameters - handle different parameter names
            # Normalize field names from PascalCase to camelCase
            normalized_params = {}
            for key, value in params.items():
                # Convert PascalCase to camelCase
                if key and key[0].isupper():
                    normalized_key = key[0].lower() + key[1:] if len(key) > 1 else key.lower()
                else:
                    normalized_key = key
                normalized_params[normalized_key] = value
            
            # Map common variations to expected names
            if 'type' in normalized_params and 'stepType' not in normalized_params:
                normalized_params['stepType'] = normalized_params.pop('type')
            if 'text' in normalized_params and 'step' not in normalized_params:
                normalized_params['step'] = normalized_params.pop('text')
            request = StepRequest(**normalized_params)
        else:
            raise ValueError(f"Invalid executeStep parameters: {params}")
        
        # Ensure parameters and context are dicts (not None)
        if request.parameters is None:
            request.parameters = {}
        if request.context is None:
            request.context = {}
            
        self._logger.info(f"Executing step: {request.stepType} {request.step}")
        
        try:
            # Execute the step with context
            result = await self._step_registry.execute_step(
                step_type=request.stepType,
                step_text=request.step,
                parameters=request.parameters,
                context=request.context
            )
            
            self._logger.info("Step executed successfully")
            
            # Collect logs and convert to LogResponse format
            collected_logs = self._logger_provider.get_all_logs()
            
            # Map string log levels to Microsoft.Extensions.Logging.LogLevel enum values
            level_map = {
                "TRACE": 0,
                "DEBUG": 1, 
                "INFO": 2,
                "INFORMATION": 2,
                "WARNING": 3,
                "WARN": 3,
                "ERROR": 4,
                "CRITICAL": 5,
                "FATAL": 5,
                "NONE": 6
            }
            
            log_responses = [
                LogResponse(
                    timestamp=datetime.utcnow().isoformat() + 'Z',
                    level=level_map.get(log.level.upper(), 2),  # Default to Information (2)
                    message=log.message
                )
                for log in collected_logs
            ]
            
            # Add collected logs to result
            if result.logs is None:
                result.logs = log_responses
            else:
                result.logs.extend(log_responses)
            
            # Convert to dict for JSON serialization matching Harmony contract
            return {
                'success': result.success,
                'error': result.error,
                'context': result.context or {},  # Use context instead of data
                'logs': [
                    {
                        'timestamp': log.timestamp,
                        'level': log.level,  # Already numeric from LogResponse
                        'message': log.message
                    }
                    for log in (result.logs or [])
                ]
            }
            
        except Exception as e:
            self._logger.error(f"Step execution failed: {e}", exc_info=True)
            
            # Get all logs including the error
            logs = self._logger_provider.get_all_logs()
            
            # Convert logs to LogResponse format with numeric log levels
            level_map = {
                "TRACE": 0,
                "DEBUG": 1, 
                "INFO": 2,
                "INFORMATION": 2,
                "WARNING": 3,
                "WARN": 3,
                "ERROR": 4,
                "CRITICAL": 5,
                "FATAL": 5,
                "NONE": 6
            }
            
            error_log_responses: List[Dict[str, Any]] = [
                {
                    'timestamp': datetime.utcnow().isoformat() + 'Z',
                    'level': level_map.get(log.level.upper(), 2),  # Default to Information (2)
                    'message': log.message
                }
                for log in logs
            ]
            
            return {
                'success': False,
                'error': str(e),
                'context': {},  # Use context instead of data
                'logs': error_log_responses
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