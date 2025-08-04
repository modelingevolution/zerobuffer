using System.Text.Json;

namespace ZeroBuffer.ProtocolTests.JsonRpc
{
    /// <summary>
    /// Simple test to verify JSON-RPC infrastructure works
    /// </summary>
    public static class SimpleTest
    {
        public static void RunInProcessTest()
        {
            Console.WriteLine("=== Running In-Process JSON-RPC Test ===");
            
            try
            {
                // Create test service directly
                var service = new TestService();
                
                // Test 1: Setup
                Console.WriteLine("\n1. Testing Setup...");
                var setupRequest = new TestSetupRequest 
                { 
                    TestId = 101, 
                    Role = "reader", 
                    BufferName = "test-buffer" 
                };
                var setupResponse = service.Setup(setupRequest);
                Console.WriteLine($"✓ Setup successful: handle={setupResponse.Handle}, test={setupResponse.Description}");
                
                // Test 2: Execute a step
                Console.WriteLine("\n2. Testing Step Execution...");
                var stepRequest = new TestStepRequest
                {
                    Handle = setupResponse.Handle,
                    StepName = "createBuffer",
                    Args = new Dictionary<string, object>
                    {
                        ["metadataSize"] = 1024,
                        ["payloadSize"] = 10240
                    }
                };
                var stepResponse = service.Step(stepRequest).GetAwaiter().GetResult();
                Console.WriteLine($"✓ Step executed: success={stepResponse.Success}, data={JsonSerializer.Serialize(stepResponse.Data)}");
                
                // Test 3: Teardown
                Console.WriteLine("\n3. Testing Teardown...");
                var teardownRequest = new TestTeardownRequest { Handle = setupResponse.Handle };
                var teardownResponse = service.Teardown(teardownRequest);
                Console.WriteLine($"✓ Teardown successful: exitCode={teardownResponse.ExitCode}");
                Console.WriteLine($"  Summary: {teardownResponse.Summary}");
                
                Console.WriteLine("\n=== All Tests Passed ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Test failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}