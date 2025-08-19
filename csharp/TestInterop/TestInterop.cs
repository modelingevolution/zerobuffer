using System;
using System.Text;
using System.Threading;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;

class TestInterop
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: TestInterop <server|client>");
            return;
        }
        
        string mode = args[0];
        string channelName = "interop_test";
        
        if (mode == "server")
        {
            RunServer(channelName);
        }
        else if (mode == "client")
        {
            RunClient(channelName);
        }
        else
        {
            Console.WriteLine("Invalid mode. Use 'server' or 'client'");
        }
    }
    
    static void RunServer(string channelName)
    {
        Console.WriteLine("C# Server: Starting on channel " + channelName);
        
        var factory = new DuplexChannelFactory();
        var config = new BufferConfig(1024, 64 * 1024); // 1KB metadata, 64KB payload
        
        var server = factory.CreateImmutableServer(channelName, config);
        
        server.Start((Frame request, Writer responseWriter) =>
        {
            // Read request data
            var requestData = request.ToArray();
            string requestStr = Encoding.UTF8.GetString(requestData);
            Console.WriteLine($"C# Server: Received '{requestStr}'");
            
            // Write response directly to writer
            string responseStr = $"C# Echo: {requestStr}";
            responseWriter.WriteFrame(Encoding.UTF8.GetBytes(responseStr));
        });
        
        Console.WriteLine("C# Server: Running for 30 seconds...");
        Thread.Sleep(30000);  // Run for 30 seconds
        
        server.Stop();
        Console.WriteLine("C# Server: Stopped");
    }
    
    static void RunClient(string channelName)
    {
        Console.WriteLine("C# Client: Connecting to channel " + channelName);
        
        var factory = new DuplexChannelFactory();
        var client = factory.CreateClient(channelName);
        
        // Give server time to initialize
        Thread.Sleep(500);
        
        // Send messages
        for (int i = 0; i < 3; i++)
        {
            string message = $"C# Message {i}";
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            
            Console.WriteLine($"C# Client: Sending '{message}'");
            var seq = client.SendRequest(messageBytes);
            Console.WriteLine($"C# Client: Sent with sequence {seq}");
            
            var response = client.ReceiveResponse(TimeSpan.FromSeconds(5));
            if (response.IsValid)
            {
                var responseData = response.ToArray();
                string responseStr = Encoding.UTF8.GetString(responseData);
                Console.WriteLine($"C# Client: Received '{responseStr}'");
            }
            else
            {
                Console.WriteLine("C# Client: Failed to receive response");
            }
            
            Thread.Sleep(100);
        }
        
        client.Dispose();
        Console.WriteLine("C# Client: Finished");
    }
}