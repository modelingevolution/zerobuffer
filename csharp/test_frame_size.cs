using System;
using ZeroBuffer;

class TestFrameSize
{
    static void Main()
    {
        Console.WriteLine("Testing frame size calculation...");
        
        // Create buffer with exactly 17 bytes
        var config = new BufferConfig
        {
            MetadataSize = 0,
            PayloadSize = 17
        };
        
        Console.WriteLine($"Creating buffer with PayloadSize={config.PayloadSize}");
        
        using var reader = new Reader("test_frame_size", config);
        using var writer = new Writer("test_frame_size");
        
        try
        {
            // Write 1-byte frame (needs 17 bytes total with header)
            Console.WriteLine("Writing 1-byte frame (17 bytes total with header)...");
            writer.WriteFrame(new byte[1]);
            Console.WriteLine("1-byte frame written successfully");
            
            // Try to write 2-byte frame (needs 18 bytes total with header)
            Console.WriteLine("Attempting to write 2-byte frame (18 bytes total with header)...");
            writer.WriteFrame(new byte[2]);
            Console.WriteLine("ERROR: 2-byte frame written successfully - should have thrown!");
        }
        catch (FrameTooLargeException)
        {
            Console.WriteLine("SUCCESS: FrameTooLargeException thrown as expected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
    }
}