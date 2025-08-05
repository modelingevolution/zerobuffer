using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ModelingEvolution.Harmony.Core;
using ModelingEvolution.Harmony.ProcessManagement;

namespace ModelingEvolution.Harmony.Tests;

/// <summary>
/// Demonstrates how the actual process communication works
/// </summary>
public class ProcessCommunicationDemoTest
{
    private readonly ITestOutputHelper _output;
    
    public ProcessCommunicationDemoTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void ShowHowProcessCommunicationWorks()
    {
        _output.WriteLine("=== How Harmony Communicates with Test Processes ===\n");
        
        _output.WriteLine("1. PROCESS STARTUP");
        _output.WriteLine("   - ProcessManager starts a subprocess using ProcessStartInfo");
        _output.WriteLine("   - The executable and arguments come from harmony-config.json");
        _output.WriteLine("   - Standard input/output are redirected for JSON-RPC communication");
        _output.WriteLine("   - Example for Python process:");
        _output.WriteLine("     ProcessStartInfo {");
        _output.WriteLine("       FileName = \"python\",");
        _output.WriteLine("       Arguments = \"zerobuffer_test_server.py serve\",");
        _output.WriteLine("       RedirectStandardInput = true,");
        _output.WriteLine("       RedirectStandardOutput = true");
        _output.WriteLine("     }");
        _output.WriteLine("");
        
        _output.WriteLine("2. JSON-RPC INITIALIZATION");
        _output.WriteLine("   - StreamJsonRpc library creates bidirectional communication");
        _output.WriteLine("   - Messages are sent as JSON over stdin/stdout");
        _output.WriteLine("   - Each message has format:");
        _output.WriteLine("     {");
        _output.WriteLine("       \"jsonrpc\": \"2.0\",");
        _output.WriteLine("       \"method\": \"executeStep\",");
        _output.WriteLine("       \"params\": { ... },");
        _output.WriteLine("       \"id\": 1");
        _output.WriteLine("     }");
        _output.WriteLine("");
        
        _output.WriteLine("3. STEP EXECUTION FLOW");
        _output.WriteLine("   a) StepExecutor receives a StepDefinition");
        _output.WriteLine("   b) Gets the IProcessConnection from ProcessManager");
        _output.WriteLine("   c) Calls InvokeAsync<T> on the connection");
        _output.WriteLine("   d) StreamJsonRpc serializes the request to JSON");
        _output.WriteLine("   e) Sends JSON to subprocess via stdin");
        _output.WriteLine("   f) Subprocess executes the step and returns result via stdout");
        _output.WriteLine("   g) StreamJsonRpc deserializes the response");
        _output.WriteLine("");
        
        ShowExampleCommunication();
    }
    
    private void ShowExampleCommunication()
    {
        _output.WriteLine("4. EXAMPLE COMMUNICATION SEQUENCE");
        _output.WriteLine("");
        
        // Example step
        var step = new
        {
            process = "writer",
            stepType = "when",
            step = "writes frame with size '1024' and sequence '1'",
            originalStep = "writes frame with size '1024' and sequence '1'",
            parameters = new Dictionary<string, string>
            {
                ["size"] = "1024",
                ["sequence"] = "1"
            }
        };
        
        _output.WriteLine("SENT TO SUBPROCESS (via stdin):");
        var request = new
        {
            jsonrpc = "2.0",
            method = "executeStep",
            @params = step,
            id = 1
        };
        _output.WriteLine(JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }));
        _output.WriteLine("");
        
        _output.WriteLine("SUBPROCESS PROCESSES THE REQUEST:");
        _output.WriteLine("- Parses the JSON");
        _output.WriteLine("- Extracts step type and parameters");
        _output.WriteLine("- Executes actual ZeroBuffer operation (write frame)");
        _output.WriteLine("- Constructs response");
        _output.WriteLine("");
        
        _output.WriteLine("RECEIVED FROM SUBPROCESS (via stdout):");
        var response = new
        {
            jsonrpc = "2.0",
            result = new
            {
                Success = true,
                Error = (string?)null,
                Data = new Dictionary<string, object>
                {
                    ["bytesWritten"] = 1024,
                    ["sequenceNumber"] = 1
                },
                Logs = new[]
                {
                    new { Level = "INFO", Message = "Frame written successfully" }
                }
            },
            id = 1
        };
        _output.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
        _output.WriteLine("");
        
        _output.WriteLine("5. KEY CLASSES INVOLVED:");
        _output.WriteLine("   - ProcessManager: Manages subprocess lifecycle");
        _output.WriteLine("   - ProcessConnection: Wraps StreamJsonRpc for a single process");
        _output.WriteLine("   - StepExecutor: Routes steps to correct process");
        _output.WriteLine("   - JsonRpc (StreamJsonRpc): Handles JSON-RPC protocol");
    }
    
    [Fact]
    public void ShowPlatformServerRequirements()
    {
        _output.WriteLine("=== Platform Test Server Requirements ===\n");
        
        _output.WriteLine("Each platform (C#, Python, C++) must implement a test server that:");
        _output.WriteLine("");
        
        _output.WriteLine("1. ACCEPTS JSON-RPC over stdin/stdout");
        _output.WriteLine("2. IMPLEMENTS these methods:");
        _output.WriteLine("   - health(): Returns true if server is ready");
        _output.WriteLine("   - executeStep(params): Executes a test step");
        _output.WriteLine("");
        
        _output.WriteLine("3. STEP EXECUTION CONTRACT:");
        _output.WriteLine("   Input:");
        _output.WriteLine("   {");
        _output.WriteLine("     \"process\": \"writer\",");
        _output.WriteLine("     \"stepType\": \"when\",");
        _output.WriteLine("     \"step\": \"writes frame...\",");
        _output.WriteLine("     \"originalStep\": \"writes frame...\",");
        _output.WriteLine("     \"parameters\": { ... }");
        _output.WriteLine("   }");
        _output.WriteLine("");
        _output.WriteLine("   Output:");
        _output.WriteLine("   {");
        _output.WriteLine("     \"Success\": true/false,");
        _output.WriteLine("     \"Error\": \"error message or null\",");
        _output.WriteLine("     \"Data\": { ... },");
        _output.WriteLine("     \"Logs\": [");
        _output.WriteLine("       { \"Level\": \"INFO\", \"Message\": \"...\" }");
        _output.WriteLine("     ]");
        _output.WriteLine("   }");
        _output.WriteLine("");
        
        _output.WriteLine("4. EXAMPLE PYTHON SERVER SKELETON:");
        _output.WriteLine(@"
import sys
import json

def handle_request(request):
    if request['method'] == 'health':
        return {'result': True}
    elif request['method'] == 'executeStep':
        params = request['params']
        # Parse step and execute ZeroBuffer operation
        return {
            'result': {
                'Success': True,
                'Error': None,
                'Data': {},
                'Logs': []
            }
        }

# Main JSON-RPC loop
while True:
    line = sys.stdin.readline()
    if not line:
        break
    request = json.loads(line)
    response = {
        'jsonrpc': '2.0',
        'id': request.get('id')
    }
    try:
        response.update(handle_request(request))
    except Exception as e:
        response['error'] = {'message': str(e)}
    sys.stdout.write(json.dumps(response) + '\n')
    sys.stdout.flush()
");
    }
}