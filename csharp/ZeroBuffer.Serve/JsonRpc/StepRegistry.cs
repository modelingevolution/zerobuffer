using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;

namespace ZeroBuffer.Serve.JsonRpc;

public class StepRegistry
{
    private readonly Dictionary<StepType, List<StepDefinitionInfo>> _steps = new();
    private readonly ILogger<StepRegistry> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    public StepRegistry(ILogger<StepRegistry> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        InitializeStepTypes();
    }
    
    private void InitializeStepTypes()
    {
        _steps[StepType.Given] = new List<StepDefinitionInfo>();
        _steps[StepType.When] = new List<StepDefinitionInfo>();
        _steps[StepType.Then] = new List<StepDefinitionInfo>();
    }
    
    public void DiscoverSteps(Assembly assembly)
    {
        _logger.LogInformation("Discovering steps in assembly: {AssemblyName}", assembly.GetName().Name);
        
        var bindingTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<BindingAttribute>() != null)
            .ToList();
        
        _logger.LogInformation("Found {Count} binding classes", bindingTypes.Count);
        
        foreach (var type in bindingTypes)
        {
            RegisterStepsFromType(type);
        }
        _logger.LogDebug("Steps registered from: {TypeName}", String.Join(", ", bindingTypes.Select(x=>x.Name)));

        // Log summary
        _logger.LogInformation("Step discovery complete: Given={Given}, When={When}, Then={Then}",
            _steps[StepType.Given].Count,
            _steps[StepType.When].Count,
            _steps[StepType.Then].Count);
    }
    
    private void RegisterStepsFromType(Type type)
    {
       
        
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check for Given attributes (can be multiple)
            var givenAttrs = method.GetCustomAttributes<GivenAttribute>();
            foreach (var givenAttr in givenAttrs)
            {
                RegisterStep(StepType.Given, givenAttr.Regex, type, method);
            }
            
            // Check for When attributes (can be multiple)
            var whenAttrs = method.GetCustomAttributes<WhenAttribute>();
            foreach (var whenAttr in whenAttrs)
            {
                RegisterStep(StepType.When, whenAttr.Regex, type, method);
            }
            
            // Check for Then attributes (can be multiple)
            var thenAttrs = method.GetCustomAttributes<ThenAttribute>();
            foreach (var thenAttr in thenAttrs)
            {
                RegisterStep(StepType.Then, thenAttr.Regex, type, method);
            }
        }
    }
    
    private void RegisterStep(StepType stepType, string pattern, Type declaringType, MethodInfo method)
    {
        var stepInfo = new StepDefinitionInfo
        {
            Pattern = pattern,
            Regex = new Regex("^" + pattern + "$", RegexOptions.Compiled),
            DeclaringType = declaringType,
            Method = method
        };
        
        _steps[stepType].Add(stepInfo);
        //_logger.LogDebug("Registered {StepType} step: {Pattern} -> {Type}.{Method}",stepType, pattern, declaringType.Name, method.Name);
    }
    
    public async Task<StepResponse> ExecuteStepAsync(string stepTypeStr, string stepText)
    {
        try
        {
            _logger.LogDebug("Executing step: {StepType} {StepText}", stepTypeStr, stepText);
            
            // Parse step type
            if (!Enum.TryParse<StepType>(stepTypeStr, true, out var stepType))
            {
                // Handle "and" steps - they can match any type
                stepType = StepType.Given; // We'll try all types
            }
            
            // Find matching step
            StepDefinitionInfo? matchingStep = null;
            Match? match = null;
            
            if (stepTypeStr.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                // For "and" steps, try all step types
                foreach (var type in Enum.GetValues<StepType>())
                {
                    (matchingStep, match) = FindMatchingStep(type, stepText);
                    if (matchingStep != null)
                    {
                        _logger.LogDebug("Found matching {StepType} step for 'and' step", type);
                        break;
                    }
                }
            }
            else
            {
                (matchingStep, match) = FindMatchingStep(stepType, stepText);
            }
            
            if (matchingStep == null || match == null)
            {
                var error = $"No matching step definition found for: {stepTypeStr} {stepText}";
                _logger.LogWarning(error);
                return new StepResponse
                {
                    Success = false,
                    Error = error
                };
            }
            
            // Execute the step
            return await ExecuteStepMethodAsync(matchingStep, match);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step: {StepType} {StepText}", stepTypeStr, stepText);
            return new StepResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    private (StepDefinitionInfo? step, Match? match) FindMatchingStep(StepType stepType, string stepText)
    {
        if (!_steps.ContainsKey(stepType))
        {
            return (null, null);
        }
        
        foreach (var step in _steps[stepType])
        {
            var match = step.Regex.Match(stepText);
            if (match.Success)
            {
                return (step, match);
            }
        }
        
        return (null, null);
    }
    
    private async Task<StepResponse> ExecuteStepMethodAsync(StepDefinitionInfo stepInfo, Match match)
    {
        try
        {
            // Create instance of the step class
            var instance = _serviceProvider.GetRequiredService(stepInfo.DeclaringType);
            
            // Extract parameters from regex groups
            var parameters = ExtractParameters(stepInfo.Method, match);
            
            // Execute the method
            var result = stepInfo.Method.Invoke(instance, parameters);
            
            // Handle async methods
            if (result is Task task)
            {
                await task;
            }
            
            return new StepResponse
            {
                Success = true,
                Logs = new List<LogEntry>
                {
                    new() { Level = "INFO", Message = $"Step executed: {stepInfo.Method.Name}" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step method: {Method}", stepInfo.Method.Name);
            
            // Get the actual exception (unwrap TargetInvocationException)
            var actualException = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            
            return new StepResponse
            {
                Success = false,
                Error = actualException.Message,
                Logs = new List<LogEntry>
                {
                    new() { Level = "ERROR", Message = actualException.Message }
                }
            };
        }
    }
    
    private object?[] ExtractParameters(MethodInfo method, Match match)
    {
        var parameterInfos = method.GetParameters();
        var parameters = new object?[parameterInfos.Length];
        
        // Groups[0] is the entire match, so parameter groups start at 1
        for (int i = 0; i < parameterInfos.Length; i++)
        {
            if (i + 1 < match.Groups.Count)
            {
                var groupValue = match.Groups[i + 1].Value;
                parameters[i] = ConvertParameter(groupValue, parameterInfos[i].ParameterType);
            }
            else
            {
                // Handle optional parameters
                if (parameterInfos[i].HasDefaultValue)
                {
                    parameters[i] = parameterInfos[i].DefaultValue;
                }
                else
                {
                    parameters[i] = null;
                }
            }
        }
        
        return parameters;
    }
    
    private object? ConvertParameter(string value, Type targetType)
    {
        if (targetType == typeof(string))
        {
            return value;
        }
        
        if (targetType == typeof(int))
        {
            return int.Parse(value);
        }
        
        if (targetType == typeof(uint))
        {
            return uint.Parse(value);
        }
        
        if (targetType == typeof(long))
        {
            return long.Parse(value);
        }
        
        if (targetType == typeof(ulong))
        {
            return ulong.Parse(value);
        }
        
        if (targetType == typeof(bool))
        {
            return bool.Parse(value);
        }
        
        if (targetType == typeof(double))
        {
            return double.Parse(value);
        }
        
        if (targetType == typeof(float))
        {
            return float.Parse(value);
        }
        
        // Add more type conversions as needed
        throw new NotSupportedException($"Parameter type {targetType.Name} is not supported");
    }
    
    public List<StepInfo> GetAllSteps()
    {
        var result = new List<StepInfo>();
        
        foreach (var kvp in _steps)
        {
            var stepType = kvp.Key;
            var stepDefinitions = kvp.Value;
            
            foreach (var stepDef in stepDefinitions)
            {
                result.Add(new StepInfo
                {
                    Type = stepType.ToString().ToLower(),
                    Pattern = stepDef.Pattern
                });
            }
        }
        
        return result.OrderBy(x => x.Type).ThenBy(x => x.Pattern).ToList();
    }
    
    private enum StepType
    {
        Given,
        When,
        Then
    }
    
    private class StepDefinitionInfo
    {
        public string Pattern { get; set; } = "";
        public Regex Regex { get; set; } = null!;
        public Type DeclaringType { get; set; } = null!;
        public MethodInfo Method { get; set; } = null!;
    }
}