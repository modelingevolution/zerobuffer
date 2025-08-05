using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;
using TechTalk.SpecFlow;
using Xunit.Abstractions;

namespace ZeroBuffer.ProtocolTests.StepDefinitions
{
    [Binding]
    public class GenericSteps
    {
        private readonly ScenarioContext _scenarioContext;
        private readonly ITestOutputHelper _outputHelper;
        private readonly Dictionary<string, ProcessContext> _processes = new();
        private readonly Dictionary<string, JsonRpcClass> _rpcConnections = new();
        private readonly TestConfiguration _configuration;
        private string _currentTarget = "";

        public GenericSteps(ScenarioContext scenarioContext, ITestOutputHelper outputHelper)
        {
            _scenarioContext = scenarioContext;
            _outputHelper = outputHelper;
            _configuration = LoadConfiguration();
        }

        #region Generic Step Definitions

        [Given(@"(.*)")]
        public async Task GivenGeneric(string step)
        {
            await ExecuteStepWithParsing("given", step);
        }

        [When(@"(.*)")]
        public async Task WhenGeneric(string step)
        {
            await ExecuteStepWithParsing("when", step);
        }

        [Then(@"(.*)")]
        public async Task ThenGeneric(string step)
        {
            await ExecuteStepWithParsing("then", step);
        }

        #endregion

        #region Private Methods

        private async Task ExecuteStepWithParsing(string stepType, string fullStep)
        {
            _outputHelper.WriteLine($"[GenericSteps] ExecuteStepWithParsing: stepType={stepType}, fullStep='{fullStep}'");
            
            // Check if this is a context-switching step
            if (TryHandleContextSwitch(stepType, fullStep))
            {
                _outputHelper.WriteLine($"[GenericSteps] Context switch handled for step: {fullStep}");
                return;
            }

            // Execute the step on the current target
            if (string.IsNullOrEmpty(_currentTarget))
            {
                _outputHelper.WriteLine($"[GenericSteps] ERROR: No target process set. Current target is empty.");
                throw new InvalidOperationException("No target process set. Use 'the [role] is '[target]'' first.");
            }

            _outputHelper.WriteLine($"[GenericSteps] Getting RPC connection for target: {_currentTarget}");
            var rpc = GetRpcConnection(_currentTarget);

            var request = new
            {
                stepType,
                step = fullStep,
                context = new
                {
                    scenarioId = _scenarioContext.ScenarioInfo.Title,
                    testId = GetTestId()
                }
            };

            try
            {
                _outputHelper.WriteLine($"[GenericSteps] Invoking executeStep via JSON-RPC with request: stepType={request.stepType}, step={request.step}");
                
                // Add timeout to prevent hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await rpc.InvokeWithCancellationAsync<StepResult>("executeStep", new[] { request }, cts.Token);
                
                _outputHelper.WriteLine($"[GenericSteps] JSON-RPC response received: Success={result.Success}, Error={result.Error}");
                
                if (!result.Success)
                {
                    throw new Exception($"Step failed: {result.Error}");
                }

                // Store result in scenario context for later steps
                if (result.Data != null)
                {
                    foreach (var kvp in result.Data)
                    {
                        _scenarioContext[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (RemoteInvocationException ex)
            {
                throw new Exception($"Remote step execution failed: {ex.Message}", ex);
            }
        }

        private bool TryHandleContextSwitch(string stepType, string step)
        {
            // Pattern: "the {role} is '{target}'"
            var match = System.Text.RegularExpressions.Regex.Match(step, @"^the (reader|writer|server|client) is '([^']+)'$");
            if (match.Success)
            {
                var role = match.Groups[1].Value;
                var target = match.Groups[2].Value;
                _outputHelper.WriteLine($"[GenericSteps] Context switch detected: role={role}, target={target}");

                if (IsConfiguredTarget(target))
                {
                    _outputHelper.WriteLine($"[GenericSteps] Target '{target}' is configured. Setting as current target.");
                    _currentTarget = target;
                    EnsureProcessStarted(target).Wait();
                    return true;
                }
                else
                {
                    _outputHelper.WriteLine($"[GenericSteps] Target '{target}' is NOT configured. Available targets: {string.Join(", ", _configuration.Targets.Keys)}");
                }
            }

            // Handle special background steps that don't need a target
            if (step == "the test mode is configured" || 
                step == "benchmark environment is prepared" ||
                step == "stress test environment is prepared")
            {
                // These are just setup steps, store in context
                _scenarioContext["testMode"] = _configuration.TestMode;
                return true;
            }

            return false;
        }

        private bool IsConfiguredTarget(string target)
        {
            return _configuration.Targets.ContainsKey(target);
        }

        private async Task EnsureProcessStarted(string target)
        {
            if (!_processes.ContainsKey(target))
            {
                _outputHelper.WriteLine($"[GenericSteps] Starting process for target: {target}");
                var targetConfig = _configuration.Targets[target];
                _outputHelper.WriteLine($"[GenericSteps] Process config: executable={targetConfig.Executable}, args={targetConfig.Arguments}, workdir={targetConfig.WorkingDirectory}");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = targetConfig.Executable,
                        Arguments = targetConfig.Arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = targetConfig.WorkingDirectory ?? Environment.CurrentDirectory
                    }
                };

                // Capture stderr output
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputHelper.WriteLine($"[GenericSteps] Process {target} stderr: {e.Data}");
                    }
                };

                _outputHelper.WriteLine($"[GenericSteps] Starting process...");
                try
                {
                    process.Start();
                    process.BeginErrorReadLine();
                    _outputHelper.WriteLine($"[GenericSteps] Process started with PID: {process.Id}");
                }
                catch (Exception ex)
                {
                    _outputHelper.WriteLine($"[GenericSteps] ERROR starting process: {ex.Message}");
                    throw;
                }
                
                _outputHelper.WriteLine($"[GenericSteps] Creating JSON-RPC connection...");
                var rpc = new JsonRpcClass(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
                rpc.StartListening();
                _outputHelper.WriteLine($"[GenericSteps] JSON-RPC connection established and listening.");

                _processes[target] = new ProcessContext { Process = process, Rpc = rpc };
                _rpcConnections[target] = rpc;

                // Give the process time to initialize
                _outputHelper.WriteLine($"[GenericSteps] Waiting {_configuration.InitializationDelay}ms for process initialization...");
                await Task.Delay(_configuration.InitializationDelay);
                _outputHelper.WriteLine($"[GenericSteps] Process initialization complete for target: {target}");
            }
            else
            {
                _outputHelper.WriteLine($"[GenericSteps] Process already running for target: {target}");
            }
        }

        private JsonRpcClass GetRpcConnection(string target)
        {
            if (!_rpcConnections.ContainsKey(target))
            {
                throw new InvalidOperationException($"No RPC connection for target '{target}'");
            }
            return _rpcConnections[target];
        }

        private string GetTestId()
        {
            // Extract test ID from scenario title (e.g., "Test 1.1 - Simple Write-Read Cycle" -> "101")
            var title = _scenarioContext.ScenarioInfo.Title;
            var match = System.Text.RegularExpressions.Regex.Match(title, @"Test (\d+)\.(\d+)");
            if (match.Success)
            {
                var section = int.Parse(match.Groups[1].Value);
                var subsection = int.Parse(match.Groups[2].Value);
                return $"{section}{subsection:00}";
            }
            return "0";
        }

        private TestConfiguration LoadConfiguration()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "testconfig.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<TestConfiguration>(json) ?? new TestConfiguration();
            }

            // Default configuration for development
            return new TestConfiguration
            {
                TestMode = "separate-process",
                DefaultTimeout = 5000,
                InitializationDelay = 500,
                Targets = new Dictionary<string, TargetConfiguration>
                {
                    ["csharp"] = new TargetConfiguration
                    {
                        Executable = "dotnet",
                        Arguments = "run --project ZeroBuffer.ProtocolTests.csproj -- serve",
                        WorkingDirectory = "."
                    },
                    ["python"] = new TargetConfiguration
                    {
                        Executable = "python",
                        Arguments = "protocol_tests.py serve",
                        WorkingDirectory = "../../python"
                    },
                    ["cpp"] = new TargetConfiguration
                    {
                        Executable = "./zerobuffer_tests",
                        Arguments = "serve",
                        WorkingDirectory = "../../cpp/build"
                    }
                }
            };
        }

        #endregion

        #region Cleanup

        [AfterScenario]
        public void Cleanup()
        {
            foreach (var process in _processes.Values)
            {
                try
                {
                    process.Rpc?.Dispose();
                    if (!process.Process.HasExited)
                    {
                        process.Process.Kill();
                        process.Process.WaitForExit(5000);
                    }
                    process.Process.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            _processes.Clear();
            _rpcConnections.Clear();
            _currentTarget = "";
        }

        #endregion

        #region Support Classes

        private class ProcessContext
        {
            public Process Process { get; set; } = null!;
            public JsonRpcClass Rpc { get; set; } = null!;
        }

        private class TestConfiguration
        {
            public string TestMode { get; set; } = "separate-process";
            public int DefaultTimeout { get; set; } = 5000;
            public int InitializationDelay { get; set; } = 500;
            public Dictionary<string, TargetConfiguration> Targets { get; set; } = new();
        }

        private class TargetConfiguration
        {
            public string Executable { get; set; } = "";
            public string Arguments { get; set; } = "";
            public string? WorkingDirectory { get; set; }
        }

        private class StepResult
        {
            public bool Success { get; set; }
            public Dictionary<string, object>? Data { get; set; }
            public string? Error { get; set; }
            public Dictionary<string, object>? Context { get; set; }
        }

        #endregion
    }
}