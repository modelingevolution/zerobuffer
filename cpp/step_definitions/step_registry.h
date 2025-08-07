#ifndef ZEROBUFFER_STEP_REGISTRY_H
#define ZEROBUFFER_STEP_REGISTRY_H

#include <string>
#include <vector>
#include <regex>
#include <functional>
#include <memory>
#include <mutex>
#include <unordered_map>

namespace zerobuffer {
namespace steps {

// Forward declaration
class TestContext;

// Step handler function type
using StepHandler = std::function<void(TestContext&, const std::vector<std::string>&)>;

// Information about a registered step
struct StepInfo {
    std::string type;     // "given", "when", "then"
    std::string pattern;  // Original pattern with {word}, {string}, {int}
};

class StepRegistry {
public:
    // Singleton access
    static StepRegistry& getInstance();
    
    // Delete copy/move constructors
    StepRegistry(const StepRegistry&) = delete;
    StepRegistry& operator=(const StepRegistry&) = delete;
    StepRegistry(StepRegistry&&) = delete;
    StepRegistry& operator=(StepRegistry&&) = delete;
    
    /**
     * Register a step definition with pattern matching
     * Pattern examples:
     *   "Given the {word} process creates buffer {string} with default configuration"
     *   "When the {word} process writes {string} to the buffer"
     *   "Then the {word} should read {string} from the buffer"
     * 
     * @param pattern Step pattern with {type} placeholders
     * @param handler Function to execute when pattern matches
     */
    void registerStep(const std::string& pattern, StepHandler handler);
    
    /**
     * Execute a step by finding matching pattern and calling handler
     * 
     * @param stepText The actual step text to execute
     * @param context Test context for state management
     * @return true if step executed successfully, false otherwise
     */
    bool executeStep(const std::string& stepText, TestContext& context);
    
    /**
     * Get all registered steps for discovery
     * 
     * @return Vector of step information
     */
    std::vector<StepInfo> getAllSteps() const;
    
    /**
     * Clear all registered steps (useful for testing)
     */
    void clear();
    
    /**
     * Register all available step definitions
     * This is the main entry point for step registration
     */
    void registerAllSteps();
    
private:
    StepRegistry() = default;
    ~StepRegistry() = default;
    
    // Convert pattern with {type} placeholders to regex
    std::string patternToRegex(const std::string& pattern) const;
    
    // Extract parameter types from pattern
    std::vector<std::string> extractParameterTypes(const std::string& pattern) const;
    
    // Step definition structure
    struct StepDefinition {
        std::string originalPattern;           // Original pattern with placeholders
        std::regex regexPattern;              // Compiled regex
        StepHandler handler;                  // Handler function
        std::vector<std::string> paramTypes;  // Parameter types from pattern
    };
    
    // Thread-safe storage
    mutable std::mutex mutex_;
    std::vector<StepDefinition> definitions_;
    
    // Pattern type mappings
    static const std::unordered_map<std::string, std::string> TYPE_PATTERNS;
};

} // namespace steps
} // namespace zerobuffer

#endif // ZEROBUFFER_STEP_REGISTRY_H