using System.Diagnostics;

namespace ZeroBuffer.ProtocolTests
{
    /// <summary>
    /// Base class for protocol tests
    /// </summary>
    public abstract class BaseProtocolTest : IProtocolTest
    {
        public abstract int TestId { get; }
        public abstract string Description { get; }
        
        public abstract Task<int> RunWriterAsync(string bufferName, CancellationToken cancellationToken = default);
        public abstract Task<int> RunReaderAsync(string bufferName, CancellationToken cancellationToken = default);
        
        public virtual async Task<int> RunBothAsync(string bufferName, CancellationToken cancellationToken = default)
        {
            var readerTask = Task.Run(async () => await RunReaderAsync(bufferName, cancellationToken), cancellationToken);
            var writerTask = Task.Run(async () => await RunWriterAsync(bufferName, cancellationToken), cancellationToken);
            
            var results = await Task.WhenAll(readerTask, writerTask);
            
            // Return 0 only if both succeeded
            return results.All(r => r == 0) ? 0 : 1;
        }
        
        protected void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test {TestId}: {message}");
        }
        
        protected void LogError(string message)
        {
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Test {TestId} ERROR: {message}");
        }
        
        protected void AssertEquals<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new TestAssertionException($"{message}. Expected: {expected}, Actual: {actual}");
            }
        }
        
        protected void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new TestAssertionException(message);
            }
        }
        
        protected void AssertFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new TestAssertionException(message);
            }
        }
        
        protected async Task WaitForProcessDeathAsync(int pid, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (process.HasExited)
                        return;
                }
                catch (ArgumentException)
                {
                    // Process doesn't exist
                    return;
                }
                
                await Task.Delay(100);
            }
            
            throw new TimeoutException($"Process {pid} did not die within {timeout}");
        }
    }
    
    public class TestAssertionException : Exception
    {
        public TestAssertionException(string message) : base(message) { }
    }
}