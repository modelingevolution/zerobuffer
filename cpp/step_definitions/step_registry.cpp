#include "step_registry.h"
#include "test_context.h"
#include "basic_communication_steps.h"
// Add more step headers here as they are implemented
// #include "edge_cases_steps.h"
// #include "error_handling_steps.h"
// #include "synchronization_steps.h"
// etc.
#include <zerobuffer/logger.h>
#include <sstream>
#include <algorithm>
#include <cstdlib>

namespace zerobuffer {
namespace steps {

// Define static type pattern mappings
const std::unordered_map<std::string, std::string> StepRegistry::TYPE_PATTERNS = {
    {"{int}", "(\\d+)"},                    // Matches integers
    {"{float}", "([+-]?\\d*\\.?\\d+)"},     // Matches floating point numbers
    {"{word}", "(\\w+)"},                   // Matches word characters
    {"{string}", "'([^']*)'"},              // Matches single-quoted strings
    {"{}", "(.*)"}                          // Matches anything
};

StepRegistry& StepRegistry::getInstance() {
    static StepRegistry instance;
    
    // Initialize logging on first access (for Google Test runs)
    // When running via zerobuffer-serve, logging is already initialized
    static bool logging_initialized = false;
    if (!logging_initialized) {
        // Check if logging is already initialized by trying to log at trace level
        // If not initialized, this is safe and will just be ignored
        ZEROBUFFER_LOG_TRACE("StepRegistry") << "Checking logging initialization";
        
        // Check for ZEROBUFFER_LOG_LEVEL environment variable
        const char* log_level_env = std::getenv("ZEROBUFFER_LOG_LEVEL");
        zerobuffer::severity_level log_level = zerobuffer::info;
        
        if (log_level_env) {
            std::string level_str(log_level_env);
            if (level_str == "TRACE") log_level = zerobuffer::trace;
            else if (level_str == "DEBUG") log_level = zerobuffer::debug;
            else if (level_str == "INFO") log_level = zerobuffer::info;
            else if (level_str == "WARNING") log_level = zerobuffer::warning;
            else if (level_str == "ERROR") log_level = zerobuffer::error;
            else if (level_str == "FATAL") log_level = zerobuffer::fatal;
        }
        
        zerobuffer::init_logging(log_level);
        logging_initialized = true;
        ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Logging initialized with level: " << log_level;
    }
    
    return instance;
}

void StepRegistry::registerStep(const std::string& pattern, StepHandler handler) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    StepDefinition def;
    def.originalPattern = pattern;
    def.regexPattern = std::regex(patternToRegex(pattern));
    def.handler = handler;
    def.paramTypes = extractParameterTypes(pattern);
    
    definitions_.push_back(std::move(def));
    
    ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Registered pattern: " << pattern;
}

bool StepRegistry::executeStep(const std::string& stepText, TestContext& context) {
    std::lock_guard<std::mutex> lock(mutex_);
    
    ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Trying to match step: '" << stepText << "' (length=" << stepText.length() << ")";
    
    // Try to match against each registered pattern
    for (const auto& def : definitions_) {
        ZEROBUFFER_LOG_DEBUG("StepRegistry") << "  Against pattern: '" << def.originalPattern << "'";
        std::smatch matches;
        if (std::regex_match(stepText, matches, def.regexPattern)) {
            // Extract parameters (skip first match which is the full string)
            std::vector<std::string> params;
            for (size_t i = 1; i < matches.size(); ++i) {
                params.push_back(matches[i].str());
            }
            
            ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Matched pattern: " << def.originalPattern;
            std::stringstream paramStr;
            for (const auto& param : params) {
                paramStr << " '" << param << "'";
            }
            ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Extracted parameters:" << paramStr.str();
            
            try {
                // Execute the handler
                def.handler(context, params);
                return true;
            } catch (const std::exception& e) {
                ZEROBUFFER_LOG_ERROR("StepRegistry") << "Step execution failed: " << e.what();
                return false;
            }
        }
    }
    
    // Only log at INFO level when step is not found - this is important for users to see
    ZEROBUFFER_LOG_INFO("StepRegistry") << "Step not found: " << stepText;
    ZEROBUFFER_LOG_INFO("StepRegistry") << "Available steps (" << definitions_.size() << " registered):";
    for (const auto& def : definitions_) {
        ZEROBUFFER_LOG_INFO("StepRegistry") << "  - " << def.originalPattern;
    }
    return false;
}

std::vector<StepInfo> StepRegistry::getAllSteps() const {
    std::lock_guard<std::mutex> lock(mutex_);
    
    std::vector<StepInfo> steps;
    for (const auto& def : definitions_) {
        StepInfo info;
        
        // Determine step type from pattern
        if (def.originalPattern.find("Given") == 0) {
            info.type = "given";
        } else if (def.originalPattern.find("When") == 0) {
            info.type = "when";
        } else if (def.originalPattern.find("Then") == 0) {
            info.type = "then";
        } else {
            info.type = "unknown";
        }
        
        info.pattern = def.originalPattern;
        steps.push_back(info);
    }
    
    return steps;
}

void StepRegistry::clear() {
    std::lock_guard<std::mutex> lock(mutex_);
    definitions_.clear();
}

std::string StepRegistry::patternToRegex(const std::string& pattern) const {
    std::string regex = pattern;
    
    // Check if the pattern already contains regex capture groups
    bool hasRegexGroups = (pattern.find("([^") != std::string::npos) || 
                          (pattern.find("(\\d") != std::string::npos) ||
                          (pattern.find("(.*)") != std::string::npos) ||
                          (pattern.find("(.+)") != std::string::npos);
    
    if (!hasRegexGroups) {
        // Only escape special characters if this is not already a regex pattern
        // Escape regex special characters (except our placeholders and parentheses for groups)
        const std::string specialChars = ".^$*+?[]\\|";  // Removed () from the list
        for (char c : specialChars) {
            size_t pos = 0;
            std::string target(1, c);
            std::string replacement = "\\" + target;
            
            while ((pos = regex.find(target, pos)) != std::string::npos) {
                // Don't escape if it's part of our placeholder
                if (pos == 0 || regex[pos - 1] != '{') {
                    regex.replace(pos, 1, replacement);
                    pos += replacement.length();
                } else {
                    pos += 1;
                }
            }
        }
    }
    
    // Replace type placeholders with regex patterns
    for (const auto& [placeholder, regexPattern] : TYPE_PATTERNS) {
        size_t pos = 0;
        while ((pos = regex.find(placeholder, pos)) != std::string::npos) {
            regex.replace(pos, placeholder.length(), regexPattern);
            pos += regexPattern.length();
        }
    }
    
    // Add anchors to ensure full match
    return "^" + regex + "$";
}

std::vector<std::string> StepRegistry::extractParameterTypes(const std::string& pattern) const {
    std::vector<std::string> types;
    
    size_t pos = 0;
    while (pos < pattern.length()) {
        size_t start = pattern.find('{', pos);
        if (start == std::string::npos) break;
        
        size_t end = pattern.find('}', start);
        if (end == std::string::npos) break;
        
        std::string type = pattern.substr(start + 1, end - start - 1);
        types.push_back(type);
        
        pos = end + 1;
    }
    
    return types;
}

void StepRegistry::registerAllSteps() {
    // Clear any existing registrations to avoid duplicates
    clear();
    
    ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Registering all available step definitions...";
    
    // Register all available step definition files
    // As new step files are implemented, add their registration functions here
    
    // Basic Communication steps
    registerBasicCommunicationSteps();
    ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Registered BasicCommunication steps";
    
    // Future step definitions - uncomment as they are implemented:
    // registerEdgeCasesSteps();
    // registerErrorHandlingSteps();
    // registerProcessLifecycleSteps();
    // registerCorruptionDetectionSteps();
    // registerSynchronizationSteps();
    // registerSystemResourcesSteps();
    // registerPlatformSpecificSteps();
    // registerPerformanceMonitoringSteps();
    // registerAdvancedErrorHandlingSteps();
    // registerStressTestsSteps();
    // registerProtocolComplianceSteps();
    // registerDuplexChannelSteps();
    // registerBenchmarksSteps();
    
    ZEROBUFFER_LOG_DEBUG("StepRegistry") << "Step registration complete. Total steps: " << definitions_.size();
}

} // namespace steps
} // namespace zerobuffer