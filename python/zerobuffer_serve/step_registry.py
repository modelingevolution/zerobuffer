"""
Step registry for discovering and executing test steps

Provides automatic discovery of step definitions using pytest-bdd decorators.
"""

import asyncio
import inspect
import re
from enum import Enum
from typing import Dict, List, Callable, Any, Optional, Tuple, Match, TYPE_CHECKING
import logging

if TYPE_CHECKING:
    from pytest_bdd import given as pytest_given, when as pytest_when, then as pytest_then, parsers as pytest_parsers
    PYTEST_BDD_AVAILABLE = True
else:
    try:
        from pytest_bdd import given as pytest_given, when as pytest_when, then as pytest_then, parsers as pytest_parsers
        PYTEST_BDD_AVAILABLE = True
    except ImportError:
        # Fallback for when pytest-bdd is not installed (e.g., in production Harmony mode)
        PYTEST_BDD_AVAILABLE = False
        # We'll discover steps differently in this case
        pytest_given = None
        pytest_when = None
        pytest_then = None
        pytest_parsers = None

from .models import StepInfo, StepResponse, LogResponse
from datetime import datetime


class StepType(Enum):
    """Step types matching Gherkin keywords"""
    GIVEN = "given"
    WHEN = "when"
    THEN = "then"


class StepDefinitionInfo:
    """Information about a registered step"""
    
    def __init__(
        self,
        pattern: str,
        regex: re.Pattern,
        method: Callable,
        instance: Any,
        step_type: StepType
    ):
        self.pattern = pattern
        self.regex = regex
        self.method = method
        self.instance = instance
        self.step_type = step_type


class StepRegistry:
    """Registry for step definitions with discovery and execution"""
    
    def __init__(self, logger: logging.Logger) -> None:
        self._logger = logger
        self._steps: Dict[StepType, List[StepDefinitionInfo]] = {
            StepType.GIVEN: [],
            StepType.WHEN: [],
            StepType.THEN: []
        }
        self._instances: List[Any] = []
        
    def register_instance(self, instance: Any) -> None:
        """Register an instance containing step definitions"""
        self._instances.append(instance)
        self._logger.debug(f"Registered instance: {instance.__class__.__name__}")
        
    def discover_steps(self) -> None:
        """Discover all step definitions from registered instances"""
        for instance in self._instances:
            self._discover_steps_from_instance(instance)
            
        # Log summary
        self._logger.info(
            f"Step discovery complete: Given={len(self._steps[StepType.GIVEN])}, "
            f"When={len(self._steps[StepType.WHEN])}, "
            f"Then={len(self._steps[StepType.THEN])}"
        )
        
    def _discover_steps_from_instance(self, instance: Any) -> None:
        """Discover steps from a single instance"""
        class_name = instance.__class__.__name__
        self._logger.debug(f"Discovering steps from: {class_name}")
        
        # Inspect all methods
        for name, method in inspect.getmembers(instance, inspect.ismethod):
            # Check for pytest-bdd decorators
            step_info = self._extract_pytest_bdd_info(method)
            if step_info:
                self._register_step(
                    step_type=step_info['type'],
                    pattern=step_info['pattern'],
                    method=method,
                    instance=instance
                )
            # Fallback: check for old-style decorators (for backward compatibility)
            elif hasattr(method, '_step_definitions'):
                for step_def in method._step_definitions:
                    self._register_step(
                        step_type=step_def['type'],
                        pattern=step_def['pattern'],
                        method=method,
                        instance=instance
                    )
    
    def _extract_pytest_bdd_info(self, method: Callable) -> Optional[Dict[str, Any]]:
        """Extract pytest-bdd decorator information from a method"""
        # pytest-bdd adds different attributes based on the decorator used
        if hasattr(method, '_pytest_bdd_step_type'):
            # This is a pytest-bdd decorated method
            step_type_str = method._pytest_bdd_step_type
            pattern = getattr(method, '_pytest_bdd_pattern', None)
            
            if pattern:
                # Map pytest-bdd step type to our StepType enum
                type_map = {
                    'given': StepType.GIVEN,
                    'when': StepType.WHEN,
                    'then': StepType.THEN
                }
                step_type = type_map.get(step_type_str.lower())
                if step_type:
                    return {
                        'type': step_type,
                        'pattern': pattern
                    }
        
        # Alternative: Check for parsers.re or parsers.parse patterns
        # pytest-bdd might store patterns differently
        for attr in ['_given', '_when', '_then']:
            if hasattr(method, attr):
                pattern_obj = getattr(method, attr)
                if hasattr(pattern_obj, 'pattern'):
                    # Extract regex pattern
                    pattern = pattern_obj.pattern
                    step_type_str = attr[1:]  # Remove leading underscore
                    type_map = {
                        'given': StepType.GIVEN,
                        'when': StepType.WHEN,
                        'then': StepType.THEN
                    }
                    return {
                        'type': type_map[step_type_str],
                        'pattern': pattern
                    }
        
        return None
                    
    def _register_step(
        self,
        step_type: StepType,
        pattern: str,
        method: Callable,
        instance: Any
    ):
        """Register a single step definition"""
        # Compile regex
        regex = re.compile(f"^{pattern}$")
        
        step_info = StepDefinitionInfo(
            pattern=pattern,
            regex=regex,
            method=method,
            instance=instance,
            step_type=step_type
        )
        
        self._steps[step_type].append(step_info)
        
        self._logger.debug(
            f"Registered {step_type.value} step: {pattern} -> "
            f"{instance.__class__.__name__}.{method.__name__}"
        )
        
    def get_all_steps(self) -> List[StepInfo]:
        """Get all registered steps for discovery"""
        result = []
        
        for step_type, steps in self._steps.items():
            for step in steps:
                result.append(StepInfo(
                    type=step_type.value,
                    pattern=step.pattern
                ))
                
        return sorted(result, key=lambda x: (x.type, x.pattern))
        
    async def execute_step(
        self,
        step_type: str,
        step_text: str,
        parameters: Optional[Dict[str, Any]] = None,
        context: Optional[Dict[str, str]] = None
    ) -> StepResponse:
        """Execute a step by matching it to a definition"""
        try:
            self._logger.debug(f"Executing step: {step_type} {step_text}")
            
            # Parse step type
            if step_type.lower() == "and":
                # For "and" steps, try all types
                matching_step, match = self._find_matching_step_any_type(step_text)
            else:
                try:
                    typed_step_type = StepType(step_type.lower())
                    matching_step, match = self._find_matching_step(typed_step_type, step_text)
                except ValueError:
                    # Invalid step type, try all
                    matching_step, match = self._find_matching_step_any_type(step_text)
                    
            if not matching_step or not match:
                error = f"No matching step definition found for: {step_type} {step_text}"
                self._logger.warning(error)
                return StepResponse(
                    success=False, 
                    error=error,
                    context={},
                    logs=[]
                )
                
            # Execute the step with context
            return await self._execute_step_method(matching_step, match, parameters, context)
            
        except Exception as e:
            self._logger.error(f"Error executing step: {e}", exc_info=True)
            return StepResponse(
                success=False, 
                error=str(e),
                context={},
                logs=[LogResponse(
                    timestamp=datetime.utcnow().isoformat() + 'Z',
                    level=4,  # Error level
                    message=str(e)
                )]
            )
            
    def _find_matching_step(
        self,
        step_type: StepType,
        step_text: str
    ) -> Tuple[Optional[StepDefinitionInfo], Optional[Match]]:
        """Find a matching step definition of a specific type"""
        for step in self._steps[step_type]:
            match = step.regex.match(step_text)
            if match:
                return step, match
        return None, None
        
    def _find_matching_step_any_type(
        self,
        step_text: str
    ) -> Tuple[Optional[StepDefinitionInfo], Optional[Match]]:
        """Find a matching step definition of any type"""
        for step_type in StepType:
            step, match = self._find_matching_step(step_type, step_text)
            if step:
                self._logger.debug(f"Found matching {step_type.value} step for 'and' step")
                return step, match
        return None, None
        
    async def _execute_step_method(
        self,
        step_info: StepDefinitionInfo,
        match: Match,
        parameters: Optional[Dict[str, Any]],
        context: Optional[Dict[str, str]]
    ) -> StepResponse:
        """Execute a step method with parameters"""
        try:
            # Extract parameters from regex groups
            method_params = self._extract_parameters(step_info.method, match, parameters, context)
            
            # Execute the method
            if asyncio.iscoroutinefunction(step_info.method):
                result = await step_info.method(*method_params)
            else:
                result = step_info.method(*method_params)
                
            # Log success
            self._logger.info(f"Step executed: {step_info.method.__name__}")
            
            # Return success with updated context
            return StepResponse(
                success=True,
                error=None,
                context=context or {},  # Return the context (potentially modified by step)
                logs=[LogResponse(
                    timestamp=datetime.utcnow().isoformat() + 'Z',
                    level=2,  # Information level
                    message=f"Step executed: {step_info.method.__name__}"
                )]
            )
            
        except Exception as e:
            self._logger.error(f"Error executing step method: {e}", exc_info=True)
            return StepResponse(
                success=False,
                error=str(e),
                context=context or {},  # Return original context on error
                logs=[LogResponse(
                    timestamp=datetime.utcnow().isoformat() + 'Z',
                    level=4,  # Error level
                    message=str(e)
                )]
            )
            
    def _extract_parameters(
        self,
        method: Callable,
        match: Match,
        extra_params: Optional[Dict[str, Any]],
        context: Optional[Dict[str, str]]
    ) -> List[Any]:
        """Extract parameters for method call"""
        sig = inspect.signature(method)
        params = list(sig.parameters.keys())
        
        # Skip 'self' parameter
        if params and params[0] == 'self':
            params = params[1:]
            
        result = []
        
        # Add regex group matches
        for i, param in enumerate(params):
            if i < len(match.groups()):
                # Get value from regex match
                value = match.group(i + 1)
                # Convert type if needed
                result.append(self._convert_parameter(value, sig.parameters[param].annotation))
            elif param == 'context' and context:
                # Special handling for context parameter
                result.append(context)
            elif param == 'parameters' and extra_params:
                # Special handling for parameters dict
                result.append(extra_params)
            elif sig.parameters[param].default != inspect.Parameter.empty:
                # Use default value
                result.append(sig.parameters[param].default)
            else:
                # No value available
                result.append(None)
                
        return result
        
    def _convert_parameter(self, value: str, param_type: type) -> Any:
        """Convert string parameter to appropriate type"""
        if param_type == str or param_type == inspect.Parameter.empty:
            return value
        elif param_type == int:
            return int(value)
        elif param_type == float:
            return float(value)
        elif param_type == bool:
            return value.lower() in ('true', 'yes', '1')
        else:
            # Try to convert, fall back to string
            try:
                return param_type(value)
            except:
                return value


# Always use our own decorators for Harmony compatibility
# These decorators ensure step discovery works correctly
def given(pattern: str):
    """Decorator for Given steps"""
    def decorator(func):
        if not hasattr(func, '_step_definitions'):
            func._step_definitions = []
        func._step_definitions.append({
            'type': StepType.GIVEN,
            'pattern': pattern
        })
        return func
    return decorator

def when(pattern: str):
    """Decorator for When steps"""
    def decorator(func):
        if not hasattr(func, '_step_definitions'):
            func._step_definitions = []
        func._step_definitions.append({
            'type': StepType.WHEN,
            'pattern': pattern
        })
        return func
    return decorator

def then(pattern: str):
    """Decorator for Then steps"""
    def decorator(func):
        if not hasattr(func, '_step_definitions'):
            func._step_definitions = []
        func._step_definitions.append({
            'type': StepType.THEN,
            'pattern': pattern
        })
        return func
    return decorator

# Placeholder for parsers compatibility
class parsers:
    @staticmethod
    def re(pattern: str):
        """Regex parser - returns pattern as-is for Harmony"""
        return pattern
    
    @staticmethod
    def parse(pattern: str):
        """Parse parser - returns pattern as-is for Harmony"""
        return pattern