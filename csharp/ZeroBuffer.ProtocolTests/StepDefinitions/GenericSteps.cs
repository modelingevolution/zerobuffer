using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Bindings;

namespace ZeroBuffer.ProtocolTests.StepDefinitions
{
    [Binding]
    public class GenericSteps
    {
        private readonly ScenarioContext _scenarioContext;
        private readonly Dictionary<string, ProcessContext> _processes = new();
        private readonly Dictionary<string, JsonRpcClass> _rpcConnections = new();
        private readonly TestConfiguration _configuration;
        private string _currentGivenTarget = "";
        private string _currentWhenTarget = "";
        private string _currentThenTarget = "";

        public GenericSteps(ScenarioContext scenarioContext)
        {
            _scenarioContext = scenarioContext;
            _configuration = LoadConfiguration();
        }

        #region Background Steps

        [Given(@"^the test mode is configured$")]
        public void GivenTheTestModeIsConfigured()
        {
            // This would read from configuration to determine test mode
            // For now, we'll use separate process mode by default
            _scenarioContext["testMode"] = "separate-process";
        }

        #endregion

        #region Context Setting Steps

        [Given(@"^the (reader|writer|server|client) is '(.+)'$")]
        [When(@"^the (reader|writer|server|client) is '(.+)'$")]
        [Then(@"^the (reader|writer|server|client) is '(.+)'$")]
        public async Task SetContext(string role, string target)
        {
            if (IsConfiguredTarget(target))
            {
                var stepType = _scenarioContext.StepContext.StepInfo.StepInstance.StepDefinitionKeyword;
                switch (stepType)
                {
                    case StepDefinitionKeyword.Given:
                        _currentGivenTarget = target;
                        break;
                    case StepDefinitionKeyword.When:
                        _currentWhenTarget = target;
                        break;
                    case StepDefinitionKeyword.Then:
                        _currentThenTarget = target;
                        break;
                }
                
                await EnsureProcessStarted(target);
            }
            else
            {
                // Not a context switch, execute as a normal step
                await ExecuteStep($"the {role} is '{target}'");
            }
        }

        #endregion

        #region Generic Step Handlers

        // Given steps - more specific patterns to avoid ambiguity
        [Given(@"^create (.+)$")]
        public async Task GivenCreate(string step)
        {
            await ExecuteStep($"create {step}");
        }
        
        [Given(@"^manually (.+)$")]
        public async Task GivenManually(string step)
        {
            await ExecuteStep($"manually {step}");
        }
        
        [Given(@"^two readers (.+)$")]
        public async Task GivenTwoReaders(string step)
        {
            await ExecuteStep($"two readers {step}");
        }
        
        [Given(@"^perform (.+)$")]
        public async Task GivenPerform(string step)
        {
            await ExecuteStep($"perform {step}");
        }
        
        [Given(@"^attempt (.+)$")]
        public async Task GivenAttempt(string step)
        {
            await ExecuteStep($"attempt {step}");
        }
        
        [Given(@"^the platform is (.+)$")]
        public async Task GivenPlatform(string platform)
        {
            await ExecuteStep($"the platform is {platform}");
        }
        
        [Given(@"^spawn (.+)$")]
        public async Task GivenSpawn(string step)
        {
            await ExecuteStep($"spawn {step}");
        }
        
        [Given(@"^create buffers with various names:$")]
        public async Task GivenCreateBuffersWithNames(TechTalk.SpecFlow.Table table)
        {
            await ExecuteStep($"create buffers with various names: [table]");
        }
        
        [Given(@"^measure (.+)$")]
        public async Task GivenMeasure(string step)
        {
            await ExecuteStep($"measure {step}");
        }
        
        [Given(@"^benchmark environment is prepared$")]
        public async Task GivenBenchmarkEnvironment()
        {
            await ExecuteStep("benchmark environment is prepared");
        }
        
        [Given(@"^buffer size is (.+)$")]
        public async Task GivenBufferSize(string size)
        {
            await ExecuteStep($"buffer size is {size}");
        }
        
        [Given(@"^create maximum (.+)$")]
        public async Task GivenCreateMaximum(string what)
        {
            await ExecuteStep($"create maximum {what}");
        }
        
        [Given(@"^stress test environment is prepared$")]
        public async Task GivenStressTestEnvironment()
        {
            await ExecuteStep("stress test environment is prepared");
        }

        // When steps - more specific patterns to avoid ambiguity
        [When(@"^connect (.+)$")]
        public async Task WhenConnect(string step)
        {
            await ExecuteStep($"connect {step}");
        }
        
        [When(@"^write (.+)$")]
        public async Task WhenWrite(string step)
        {
            await ExecuteStep($"write {step}");
        }
        
        [When(@"^read (.+)$")]
        public async Task WhenRead(string step)
        {
            await ExecuteStep($"read {step}");
        }
        
        [When(@"^attempt (.+)$")]
        public async Task WhenAttempt(string step)
        {
            await ExecuteStep($"attempt {step}");
        }
        
        [When(@"^simulate (.+)$")]
        public async Task WhenSimulate(string step)
        {
            await ExecuteStep($"simulate {step}");
        }
        
        [When(@"^fill (.+)$")]
        public async Task WhenFill(string step)
        {
            await ExecuteStep($"fill {step}");
        }
        
        [When(@"^send (.+)$")]
        public async Task WhenSend(string step)
        {
            await ExecuteStep($"send {step}");
        }
        
        [When(@"^spawn (.+)$")]
        public async Task WhenSpawn(string step)
        {
            await ExecuteStep($"spawn {step}");
        }
        
        [When(@"^start (.+)$")]
        public async Task WhenStart(string step)
        {
            await ExecuteStep($"start {step}");
        }
        
        [When(@"^create (.+)$")]
        public async Task WhenCreate(string step)
        {
            await ExecuteStep($"create {step}");
        }
        
        [When(@"^dispose (.+)$")]
        public async Task WhenDispose(string step)
        {
            await ExecuteStep($"dispose {step}");
        }
        
        [When(@"^do not (.+)$")]
        public async Task WhenDoNot(string step)
        {
            await ExecuteStep($"do not {step}");
        }
        
        [When(@"^acquire (.+)$")]
        public async Task WhenAcquire(string step)
        {
            await ExecuteStep($"acquire {step}");
        }
        
        [When(@"^commit (.+)$")]
        public async Task WhenCommit(string step)
        {
            await ExecuteStep($"commit {step}");
        }
        
        [When(@"^test (.+)$")]
        public async Task WhenTest(string step)
        {
            await ExecuteStep($"test {step}");
        }
        
        [When(@"^monitor (.+)$")]
        public async Task WhenMonitor(string step)
        {
            await ExecuteStep($"monitor {step}");
        }
        
        [When(@"^measure (.+)$")]
        public async Task WhenMeasure(string step)
        {
            await ExecuteStep($"measure {step}");
        }
        
        [When(@"^compare (.+)$")]
        public async Task WhenCompare(string step)
        {
            await ExecuteStep($"compare {step}");
        }
        
        [When(@"^reach (.+)$")]
        public async Task WhenReach(string step)
        {
            await ExecuteStep($"reach {step}");
        }
        
        [When(@"^cleanup (.+)$")]
        public async Task WhenCleanup(string step)
        {
            await ExecuteStep($"cleanup {step}");
        }
        
        [When(@"^both (.+)$")]
        public async Task WhenBoth(string step)
        {
            await ExecuteStep($"both {step}");
        }
        
        [When(@"^kill (.+)$")]
        public async Task WhenKill(string step)
        {
            await ExecuteStep($"kill {step}");
        }
        
        [When(@"^graceful (.+)$")]
        public async Task WhenGraceful(string step)
        {
            await ExecuteStep($"graceful {step}");
        }
        
        [When(@"^test continuous_free_bytes calculation with:$")]
        public async Task WhenTestContinuousFreeBytes(TechTalk.SpecFlow.Table table)
        {
            await ExecuteStep($"test continuous_free_bytes calculation with: [table]");
        }

        // Then steps - more specific patterns to avoid ambiguity  
        [Then(@"^read (.+)$")]
        public async Task ThenRead(string step)
        {
            await ExecuteStep($"read {step}");
        }
        
        [Then(@"^write (.+)$")]
        public async Task ThenWrite(string step)
        {
            await ExecuteStep($"write {step}");
        }
        
        [Then(@"^frame (.+)$")]
        public async Task ThenFrame(string step)
        {
            await ExecuteStep($"frame {step}");
        }
        
        [Then(@"^signal (.+)$")]
        public async Task ThenSignal(string step)
        {
            await ExecuteStep($"signal {step}");
        }
        
        [Then(@"^all (.+)$")]
        public async Task ThenAll(string step)
        {
            await ExecuteStep($"all {step}");
        }
        
        [Then(@"^next (.+)$")]
        public async Task ThenNext(string step)
        {
            await ExecuteStep($"next {step}");
        }
        
        [Then(@"^buffer (.+)$")]
        public async Task ThenBuffer(string step)
        {
            await ExecuteStep($"buffer {step}");
        }
        
        [Then(@"^writer should (.+)$")]
        public async Task ThenWriterShould(string step)
        {
            await ExecuteStep($"writer should {step}");
        }
        
        [Then(@"^reader should (.+)$")]
        public async Task ThenReaderShould(string step)
        {
            await ExecuteStep($"reader should {step}");
        }
        
        [Then(@"^wait (.+)$")]
        public async Task ThenWait(string step)
        {
            await ExecuteStep($"wait {step}");
        }
        
        [Then(@"^connection should (.+)$")]
        public async Task ThenConnectionShould(string step)
        {
            await ExecuteStep($"connection should {step}");
        }
        
        [Then(@"^metadata (.+)$")]
        public async Task ThenMetadata(string step)
        {
            await ExecuteStep($"metadata {step}");
        }
        
        [Then(@"^second (.+)$")]
        public async Task ThenSecond(string step)
        {
            await ExecuteStep($"second {step}");
        }
        
        [Then(@"^original (.+)$")]
        public async Task ThenOriginal(string step)
        {
            await ExecuteStep($"original {step}");
        }
        
        [Then(@"^corrupt (.+)$")]
        public async Task ThenCorrupt(string step)
        {
            await ExecuteStep($"corrupt {step}");
        }
        
        [Then(@"^error should (.+)$")]
        public async Task ThenErrorShould(string step)
        {
            await ExecuteStep($"error should {step}");
        }
        
        [Then(@"^throw (.+)$")]
        public async Task ThenThrow(string step)
        {
            await ExecuteStep($"throw {step}");
        }
        
        [Then(@"^stale (.+)$")]
        public async Task ThenStale(string step)
        {
            await ExecuteStep($"stale {step}");
        }
        
        [Then(@"^old (.+)$")]
        public async Task ThenOld(string step)
        {
            await ExecuteStep($"old {step}");
        }
        
        [Then(@"^new (.+)$")]
        public async Task ThenNew(string step)
        {
            await ExecuteStep($"new {step}");
        }
        
        [Then(@"^creation should (.+)$")]
        public async Task ThenCreationShould(string step)
        {
            await ExecuteStep($"creation should {step}");
        }
        
        [Then(@"^appropriate (.+)$")]
        public async Task ThenAppropriate(string step)
        {
            await ExecuteStep($"appropriate {step}");
        }
        
        [Then(@"^partial (.+)$")]
        public async Task ThenPartial(string step)
        {
            await ExecuteStep($"partial {step}");
        }
        
        [Then(@"^client should (.+)$")]
        public async Task ThenClientShould(string step)
        {
            await ExecuteStep($"client should {step}");
        }
        
        [Then(@"^server should (.+)$")]
        public async Task ThenServerShould(string step)
        {
            await ExecuteStep($"server should {step}");
        }
        
        [Then(@"^response should (.+)$")]
        public async Task ThenResponseShould(string step)
        {
            await ExecuteStep($"response should {step}");
        }
        
        [Then(@"^responses should (.+)$")]
        public async Task ThenResponsesShould(string step)
        {
            await ExecuteStep($"responses should {step}");
        }
        
        [Then(@"^each (.+)$")]
        public async Task ThenEach(string step)
        {
            await ExecuteStep($"each {step}");
        }
        
        [Then(@"^no (.+)$")]
        public async Task ThenNo(string step)
        {
            await ExecuteStep($"no {step}");
        }
        
        [Then(@"^total (.+)$")]
        public async Task ThenTotal(string step)
        {
            await ExecuteStep($"total {step}");
        }
        
        [Then(@"^verify (.+)$")]
        public async Task ThenVerify(string step)
        {
            await ExecuteStep($"verify {step}");
        }
        
        [Then(@"^system (.+)$")]
        public async Task ThenSystem(string step)
        {
            await ExecuteStep($"system {step}");
        }
        
        [Then(@"^only (.+)$")]
        public async Task ThenOnly(string step)
        {
            await ExecuteStep($"only {step}");
        }
        
        [Then(@"^other (.+)$")]
        public async Task ThenOther(string step)
        {
            await ExecuteStep($"other {step}");
        }
        
        [Then(@"^calculations should (.+)$")]
        public async Task ThenCalculationsShould(string step)
        {
            await ExecuteStep($"calculations should {step}");
        }
        
        [Then(@"^both (.+)$")]
        public async Task ThenBoth(string step)
        {
            await ExecuteStep($"both {step}");
        }
        
        [Then(@"^mutable (.+)$")]
        public async Task ThenMutable(string step)
        {
            await ExecuteStep($"mutable {step}");
        }
        
        [Then(@"^immutable (.+)$")]
        public async Task ThenImmutable(string step)
        {
            await ExecuteStep($"immutable {step}");
        }
        
        [Then(@"^continue (.+)$")]
        public async Task ThenContinue(string step)
        {
            await ExecuteStep($"continue {step}");
        }
        
        [Then(@"^complete (.+)$")]
        public async Task ThenComplete(string step)
        {
            await ExecuteStep($"complete {step}");
        }
        
        [Then(@"^detect (.+)$")]
        public async Task ThenDetect(string step)
        {
            await ExecuteStep($"detect {step}");
        }
        
        [Then(@"^attempt (.+)$")]
        public async Task ThenAttempt(string step)
        {
            await ExecuteStep($"attempt {step}");
        }
        
        [Then(@"^permission (.+)$")]
        public async Task ThenPermission(string step)
        {
            await ExecuteStep($"permission {step}");
        }
        
        [Then(@"^report (.+)$")]
        public async Task ThenReport(string step)
        {
            await ExecuteStep($"report {step}");
        }
        
        [Then(@"^calculate (.+)$")]
        public async Task ThenCalculate(string step)
        {
            await ExecuteStep($"calculate {step}");
        }
        
        [Then(@"^expect (.+)$")]
        public async Task ThenExpect(string step)
        {
            await ExecuteStep($"expect {step}");
        }
        
        [Then(@"^measure (.+)$")]
        public async Task ThenMeasure(string step)
        {
            await ExecuteStep($"measure {step}");
        }
        
        [Then(@"^monitor (.+)$")]
        public async Task ThenMonitor(string step)
        {
            await ExecuteStep($"monitor {step}");
        }
        
        [Then(@"^ensure (.+)$")]
        public async Task ThenEnsure(string step)
        {
            await ExecuteStep($"ensure {step}");
        }
        
        [Then(@"^handle (.+)$")]
        public async Task ThenHandle(string step)
        {
            await ExecuteStep($"handle {step}");
        }
        
        [Then(@"^test (.+)$")]
        public async Task ThenTest(string step)
        {
            await ExecuteStep($"test {step}");
        }
        
        [Then(@"^proper (.+)$")]
        public async Task ThenProper(string step)
        {
            await ExecuteStep($"proper {step}");
        }
        
        [Then(@"^coalesced (.+)$")]
        public async Task ThenCoalesced(string step)
        {
            await ExecuteStep($"coalesced {step}");
        }
        
        [Then(@"^semaphore (.+)$")]
        public async Task ThenSemaphore(string step)
        {
            await ExecuteStep($"semaphore {step}");
        }
        
        [Then(@"^after each (.+)$")]
        public async Task ThenAfterEach(string step)
        {
            await ExecuteStep($"after each {step}");
        }

        #endregion

        #region Private Methods

        private async Task ExecuteStep(string step)
        {
            var currentTarget = GetCurrentTarget();
            if (string.IsNullOrEmpty(currentTarget))
            {
                throw new InvalidOperationException("No target process set. Use 'Given/When/Then the [role] is '[target]'' first.");
            }

            var rpc = GetRpcConnection(currentTarget);
            var stepType = _scenarioContext.StepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString().ToLower();

            var request = new
            {
                stepType,
                step,
                context = new
                {
                    scenarioId = _scenarioContext.ScenarioInfo.Title,
                    testId = GetTestId()
                }
            };

            try
            {
                var result = await rpc.InvokeAsync<StepResult>("executeStep", request);
                
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

        private string GetCurrentTarget()
        {
            var keyword = _scenarioContext.StepContext.StepInfo.StepInstance.StepDefinitionKeyword;
            return keyword switch
            {
                StepDefinitionKeyword.Given => _currentGivenTarget,
                StepDefinitionKeyword.When => _currentWhenTarget,
                StepDefinitionKeyword.Then => _currentThenTarget,
                _ => ""
            };
        }

        private bool IsConfiguredTarget(string target)
        {
            return _configuration.Targets.ContainsKey(target);
        }

        private async Task EnsureProcessStarted(string target)
        {
            if (!_processes.ContainsKey(target))
            {
                var targetConfig = _configuration.Targets[target];
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
                        RedirectStandardError = true
                    }
                };

                process.Start();
                
                var rpc = new JsonRpcClass(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
                rpc.StartListening();

                _processes[target] = new ProcessContext { Process = process, Rpc = rpc };
                _rpcConnections[target] = rpc;

                // Give the process time to initialize
                await Task.Delay(500);
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
            // For now, return a hardcoded configuration
            // In a real implementation, this would load from a JSON file
            return new TestConfiguration
            {
                Targets = new Dictionary<string, TargetConfiguration>
                {
                    ["csharp"] = new TargetConfiguration
                    {
                        Executable = "dotnet",
                        Arguments = "run --project ../ZeroBuffer.ProtocolTests -- serve"
                    },
                    ["python"] = new TargetConfiguration
                    {
                        Executable = "python",
                        Arguments = "../../python/protocol_tests.py serve"
                    },
                    ["cpp"] = new TargetConfiguration
                    {
                        Executable = "../../cpp/build/zerobuffer_tests",
                        Arguments = "serve"
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
            public Dictionary<string, TargetConfiguration> Targets { get; set; } = new();
        }

        private class TargetConfiguration
        {
            public string Executable { get; set; } = "";
            public string Arguments { get; set; } = "";
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