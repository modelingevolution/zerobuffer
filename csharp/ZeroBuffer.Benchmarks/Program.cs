using System;
using System.Threading.Tasks;

namespace ZeroBuffer.Benchmarks
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("ZeroBuffer Cross-Process Round-Trip Latency Benchmark");
            Console.WriteLine("=====================================================");
            Console.WriteLine();
            
            await CrossProcessPerformanceTests.RunAllTests();
        }
    }
}