using System.Collections.Concurrent;

namespace ZeroBuffer.ProtocolTests
{
    /// <summary>
    /// Registry for all protocol tests
    /// </summary>
    public class TestRegistry
    {
        private readonly ConcurrentDictionary<int, IProtocolTest> _tests = new();
        private static readonly TestRegistry _instance = new();
        
        public static TestRegistry Instance => _instance;
        
        /// <summary>
        /// Register a test
        /// </summary>
        public void Register(IProtocolTest test)
        {
            if (!_tests.TryAdd(test.TestId, test))
            {
                throw new InvalidOperationException($"Test {test.TestId} is already registered");
            }
        }
        
        /// <summary>
        /// Get a test by ID
        /// </summary>
        public IProtocolTest? Get(int testId)
        {
            return _tests.TryGetValue(testId, out var test) ? test : null;
        }
        
        /// <summary>
        /// Get all registered tests
        /// </summary>
        public IEnumerable<IProtocolTest> GetAll()
        {
            return _tests.Values.OrderBy(t => t.TestId);
        }
        
        /// <summary>
        /// Discover and register all tests via reflection
        /// </summary>
        public static void DiscoverAndRegisterAll()
        {
            Instance.DiscoverAndRegister();
        }
        
        private void DiscoverAndRegister()
        {
            var testTypes = GetType().Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(IProtocolTest).IsAssignableFrom(t))
                .ToList();
            
            foreach (var type in testTypes)
            {
                try
                {
                    var test = (IProtocolTest)Activator.CreateInstance(type)!;
                    Register(test);
                }
                catch
                {
                    // Skip tests that can't be instantiated
                }
            }
        }
        
        // Static helper methods for backward compatibility
        public static IProtocolTest? GetTest(int testId) => Instance.Get(testId);
        public static IEnumerable<IProtocolTest> GetAllTests() => Instance.GetAll();
        
        /// <summary>
        /// Initialize all tests
        /// </summary>
        public static void Initialize()
        {
            // Basic Communication Tests
            Instance.Register(new Tests.BasicCommunication.Test_101_SimpleWriteRead());
            Instance.Register(new Tests.BasicCommunication.Test_102_MultipleFramesSequential());
            Instance.Register(new Tests.BasicCommunication.Test_103_BufferFullHandling());
            
            // Process Lifecycle Tests
            Instance.Register(new Tests.ProcessLifecycle.Test_201_WriterCrashDetection());
            Instance.Register(new Tests.ProcessLifecycle.Test_202_ReaderCrashDetection());
            Instance.Register(new Tests.ProcessLifecycle.Test_203_ReaderReplacementAfterCrash());
            
            // Duplex Channel Tests
            Instance.Register(new Tests.DuplexChannel.Test_1401_BasicRequestResponse());
            Instance.Register(new Tests.DuplexChannel.Test_1403_ConcurrentClientOperations());
            Instance.Register(new Tests.DuplexChannel.Test_1404_ServerProcessingModeSingleThread());
            Instance.Register(new Tests.DuplexChannel.Test_1405_MutableVsImmutableServer());
            Instance.Register(new Tests.DuplexChannel.Test_1406_ServerDeathDuringProcessing());
            Instance.Register(new Tests.DuplexChannel.Test_1407_ClientDeathDuringResponseWait());
            Instance.Register(new Tests.DuplexChannel.Test_1408_BufferFullOnResponseChannel());
            Instance.Register(new Tests.DuplexChannel.Test_1409_ZeroCopyClientOperations());
            Instance.Register(new Tests.DuplexChannel.Test_1410_ChannelCleanupOnDispose());
        }
    }
}