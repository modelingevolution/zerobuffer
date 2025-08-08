/**
 * zerobuffer-serve - JSON-RPC server for Harmony test orchestration
 * 
 * Iteration 4: Full Harmony Protocol Compliance
 * - Content-Length header support (LSP-style)
 * - 30-second timeout for step execution
 * - Proper JSON-RPC error handling
 * - Fixed logging to stderr before global objects are created
 */

#include "step_definitions/step_registry.h"
#include "step_definitions/test_context.h"
#include "log_collector.h"

#include <zerobuffer/logger.h>
#include <nlohmann/json.hpp>
#include <iostream>
#include <string>
#include <exception>
#include <sstream>
#include <future>
#include <chrono>
#include <thread>
#include <cstdlib>

#ifdef _WIN32
#include <io.h>
#include <fcntl.h>
#else
#include <unistd.h>
#endif
#include <algorithm>
#include <cctype>

using json = nlohmann::json;
using namespace zerobuffer::steps;

// Helper function to get JSON value case-insensitively
std::string getJsonStringCaseInsensitive(const json& obj, const std::string& key, const std::string& defaultValue = "") {
    if (!obj.is_object()) {
        return defaultValue;
    }
    
    // First try exact match
    if (obj.contains(key)) {
        auto val = obj[key];
        return val.is_string() ? val.get<std::string>() : defaultValue;
    }
    
    // Try case-insensitive match
    std::string lowerKey = key;
    std::transform(lowerKey.begin(), lowerKey.end(), lowerKey.begin(), ::tolower);
    
    for (auto& [objKey, value] : obj.items()) {
        std::string lowerObjKey = objKey;
        std::transform(lowerObjKey.begin(), lowerObjKey.end(), lowerObjKey.begin(), ::tolower);
        
        if (lowerObjKey == lowerKey) {
            return value.is_string() ? value.get<std::string>() : defaultValue;
        }
    }
    
    return defaultValue;
}

// Initialize logging before any global objects are created
// This ensures that BufferNamingService logs go to stderr, not stdout
namespace {
    struct LogInitializer {
        LogInitializer() {
            // Check for ZEROBUFFER_LOG_LEVEL environment variable
            const char* log_level_env = std::getenv("ZEROBUFFER_LOG_LEVEL");
            boost::log::trivial::severity_level log_level = boost::log::trivial::info;
            
            if (log_level_env) {
                std::string level_str(log_level_env);
                if (level_str == "TRACE") log_level = boost::log::trivial::trace;
                else if (level_str == "DEBUG") log_level = boost::log::trivial::debug;
                else if (level_str == "INFO") log_level = boost::log::trivial::info;
                else if (level_str == "WARNING") log_level = boost::log::trivial::warning;
                else if (level_str == "ERROR") log_level = boost::log::trivial::error;
                else if (level_str == "FATAL") log_level = boost::log::trivial::fatal;
            }
            
            // Initialize logging to stderr
            zerobuffer::init_logging(log_level);
        }
    };
    // This will be initialized before g_testContext
    static LogInitializer g_logInit;
}

// Global test context (will be managed better in iteration 5)
static TestContext g_testContext;

// Timeout for step execution (30 seconds as per Harmony spec)
static constexpr auto STEP_TIMEOUT = std::chrono::seconds(30);

/**
 * Read Content-Length header and JSON body using C++ streams
 */
std::string readJsonRequest() {
    std::string line;
    size_t contentLength = 0;
    
    ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Reading request headers...";
    
    // Read headers
    while (std::getline(std::cin, line)) {
        // Remove CR if present (for Windows compatibility)
        if (!line.empty() && line.back() == '\r') {
            line.pop_back();
        }
        
        ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Header line: '" << line << "'";
        
        // Empty line marks end of headers
        if (line.empty()) {
            ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "End of headers";
            break;
        }
        
        // Parse Content-Length header
        if (line.find("Content-Length: ") == 0) {
            contentLength = std::stoul(line.substr(16));
            ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Content-Length: " << contentLength;
        }
    }
    
    // If no Content-Length, return empty
    if (contentLength == 0) {
        ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "No Content-Length, returning empty";
        return "";
    }
    
    // Read JSON body
    std::string jsonBody(contentLength, '\0');
    std::cin.read(&jsonBody[0], contentLength);
    
    // Check if read was successful
    if (std::cin.gcount() != static_cast<std::streamsize>(contentLength)) {
        ZEROBUFFER_LOG_ERROR("zerobuffer-serve") << "Failed to read full body. Expected " 
                                                  << contentLength << " bytes, got " 
                                                  << std::cin.gcount();
        return "";
    }
    
    ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Read body (" << contentLength << " bytes): " << jsonBody;
    
    return jsonBody;
}

/**
 * Write JSON response with Content-Length header
 */
void writeJsonResponse(const json& response) {
    std::string jsonStr = response.dump();
    
    // Write Content-Length header
    std::cout << "Content-Length: " << jsonStr.length() << "\r\n";
    std::cout << "\r\n";  // Empty line to separate headers from body
    
    // Write JSON body
    std::cout << jsonStr;
    std::cout.flush();  // Ensure immediate output
}

/**
 * Execute step with timeout and log collection
 */
json executeStepWithTimeout(const std::string& stepText) {
    json result = {
        {"success", false},
        {"data", json::object()},
        {"logs", json::array()}
    };
    
    // Clear any previous logs and start collecting
    auto& logCollector = zerobuffer::serve::get_log_collector();
    logCollector.clear_logs();
    logCollector.start_collecting();
    
    try {
        // Launch step execution asynchronously
        auto future = std::async(std::launch::async, [&stepText]() {
            return StepRegistry::getInstance().executeStep(stepText, g_testContext);
        });
        
        // Wait for completion with timeout
        if (future.wait_for(STEP_TIMEOUT) == std::future_status::timeout) {
            ZEROBUFFER_LOG_ERROR("zerobuffer-serve") << "Step execution timeout after 30 seconds: " << stepText;
            result["error"] = "Step execution timeout after 30 seconds";
            result["timeout"] = true;
        } else {
            bool success = future.get();
            result["success"] = success;
            if (!success) {
                result["error"] = "Step execution failed";
            } else {
                result["error"] = json();  // null
            }
        }
    } catch (const std::exception& e) {
        ZEROBUFFER_LOG_ERROR("zerobuffer-serve") << "Step execution exception: " << e.what();
        result["error"] = std::string("Exception: ") + e.what();
    }
    
    // Collect all logs generated during step execution
    auto logs = logCollector.get_logs_as_json();
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Collected " << logs.size() << " log entries for step: " << stepText;
    result["logs"] = logs;
    
    return result;
}

/**
 * Handle JSON-RPC request
 */
json handleRequest(const json& request) {
    json response;
    
    // Basic JSON-RPC structure
    response["jsonrpc"] = "2.0";
    
    // Copy request ID if present
    if (request.contains("id")) {
        response["id"] = request["id"];
    }
    
    try {
        // Get method name
        std::string method = request.value("method", "");
        
        if (method == "executeStep") {
            // Extract step parameters
            // StreamJsonRpc sends a single object parameter for methods
            json params = request.value("params", json());
            std::string stepText;
            std::string stepType;
            
            // Log the actual params structure for debugging
            ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Received params: " << params.dump();
            
            if (params.is_array() && params.size() == 1 && params[0].is_object()) {
                // StreamJsonRpc wraps the object in an array: [{"stepType": "Given", "step": "..."}]
                json stepRequest = params[0];
                
                // Log all fields in the request
                ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Step request fields:";
                for (auto& [key, value] : stepRequest.items()) {
                    if (value.is_string()) {
                        ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "  " << key << ": " << value.get<std::string>();
                    } else {
                        ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "  " << key << ": " << value.dump();
                    }
                }
                
                // Get fields - try PascalCase first (C# default), then camelCase
                stepType = getJsonStringCaseInsensitive(stepRequest, "StepType");
                if (stepType.empty()) {
                    stepType = getJsonStringCaseInsensitive(stepRequest, "stepType");
                }
                
                stepText = getJsonStringCaseInsensitive(stepRequest, "Step");
                if (stepText.empty()) {
                    stepText = getJsonStringCaseInsensitive(stepRequest, "step");
                }
                
                // Handle case-insensitive stepType
                if (!stepType.empty()) {
                    std::transform(stepType.begin(), stepType.end(), stepType.begin(), ::tolower);
                    stepType[0] = std::toupper(stepType[0]);
                }
            } else if (params.is_object()) {
                // Direct object format: {"StepType": "Given", "Step": "..."} or {"stepType": "Given", "step": "..."}
                // Get fields - try PascalCase first (C# default), then camelCase
                stepType = getJsonStringCaseInsensitive(params, "StepType");
                if (stepType.empty()) {
                    stepType = getJsonStringCaseInsensitive(params, "stepType");
                }
                
                stepText = getJsonStringCaseInsensitive(params, "Step");
                if (stepText.empty()) {
                    stepText = getJsonStringCaseInsensitive(params, "step");
                }
                
                // Handle case-insensitive stepType
                if (!stepType.empty()) {
                    std::transform(stepType.begin(), stepType.end(), stepType.begin(), ::tolower);
                    stepType[0] = std::toupper(stepType[0]);
                }
            } else {
                response["error"] = {
                    {"code", -32602},
                    {"message", "Invalid params: expected object or array with object"}
                };
                return response;
            }
            
            if (stepText.empty()) {
                response["error"] = {
                    {"code", -32602},
                    {"message", "Invalid params: missing step text"}
                };
                return response;
            }
            
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Executing step: " << stepText;
            
            // Execute the step with timeout
            auto stepResult = executeStepWithTimeout(stepText);
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Step result logs count: " << stepResult["logs"].size();
            
            // Check if this is a direct call (integration test) or wrapped call (Harmony)
            if (params.is_object() && !params.contains("stepType")) {
                // Direct call from integration test - return result directly
                ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Direct call detected - returning result directly";
                response["result"] = stepResult;
            } else {
                // Call from Harmony with extra params - also wrap in result
                ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Harmony call detected - wrapping in result";
                response["result"] = stepResult;
            }
            
        } else if (method == "health") {
            // Simple health check
            response["result"] = true;
            
        } else if (method == "initialize") {
            // Initialize test context
            g_testContext.reset();
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Test context initialized";
            
            // Extract test info if provided
            json params = request.value("params", json::object());
            
            // Handle both array-wrapped and direct params
            if (params.is_array() && params.size() == 1 && params[0].is_object()) {
                params = params[0];
            }
            
            // Get testName case-insensitively
            std::string testName = getJsonStringCaseInsensitive(params, "testName");
            if (!testName.empty()) {
                ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Running test: " << testName;
            }
            
            response["result"] = true;
            
        } else if (method == "discover") {
            // Return list of registered steps
            auto steps = StepRegistry::getInstance().getAllSteps();
            json stepList = json::array();
            
            for (const auto& step : steps) {
                json stepInfo = {
                    {"pattern", step.pattern},
                    {"type", step.type}
                };
                stepList.push_back(stepInfo);
            }
            
            response["result"] = {
                {"steps", stepList},
                {"capabilities", {
                    {"timeout", true},
                    {"contentLength", true},
                    {"logging", true}
                }}
            };
            
        } else if (method == "cleanup") {
            // Clean up test context
            g_testContext.reset();
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Test context cleaned up";
            response["result"] = json();  // null result
            
        } else if (method == "shutdown") {
            // Prepare for shutdown
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Shutdown requested";
            response["result"] = json();  // null result
            // Will exit after sending response
            
        } else {
            // Unknown method
            response["error"] = {
                {"code", -32601},
                {"message", "Method not found: " + method}
            };
        }
        
    } catch (const json::exception& e) {
        response["error"] = {
            {"code", -32700},
            {"message", "Parse error: " + std::string(e.what())}
        };
    } catch (const std::exception& e) {
        response["error"] = {
            {"code", -32603},
            {"message", "Internal error: " + std::string(e.what())}
        };
    }
    
    return response;
}

int main() {
    // Set stdin/stdout to binary mode on Windows
#ifdef _WIN32
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
#endif
    
    // Logging already initialized by LogInitializer
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Starting JSON-RPC server (Iteration 4 - Harmony Compliant)";
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Protocol: Content-Length headers, 30-second timeout";
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "PID: " << getpid();
    
    // Register all available step definitions
    StepRegistry::getInstance().registerAllSteps();
    
    // Log all registered steps for debugging
    auto allSteps = StepRegistry::getInstance().getAllSteps();
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Step definitions registered: " 
                                             << allSteps.size();
    for (const auto& step : allSteps) {
        ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "  Pattern: " << step.pattern;
    }
    
    // Main loop - read JSON with Content-Length headers
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Entering main loop";
    while (true) {
        try {
            ZEROBUFFER_LOG_DEBUG("zerobuffer-serve") << "Waiting for request...";
            
            // Read request (with Content-Length header)
            std::string jsonBody = readJsonRequest();
            
            // If no body read, check for EOF
            if (jsonBody.empty()) {
                if (std::cin.eof()) {
                    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Stream closed, shutting down";
                    break;
                }
                // Could be a temporary issue, continue
                continue;
            }
            
            // Parse JSON request
            json request = json::parse(jsonBody);
            
            // Handle the request
            json response = handleRequest(request);
            
            // Send response with Content-Length header
            writeJsonResponse(response);
            
            // Check if shutdown was requested
            if (request.value("method", "") == "shutdown") {
                ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Shutting down...";
                break;
            }
            
        } catch (const json::exception& e) {
            // Send parse error response
            json errorResponse = {
                {"jsonrpc", "2.0"},
                {"error", {
                    {"code", -32700},
                    {"message", "Parse error: " + std::string(e.what())}
                }},
                {"id", nullptr}
            };
            writeJsonResponse(errorResponse);
        } catch (const std::exception& e) {
            // Send internal error response
            json errorResponse = {
                {"jsonrpc", "2.0"},
                {"error", {
                    {"code", -32603},
                    {"message", "Internal error: " + std::string(e.what())}
                }},
                {"id", nullptr}
            };
            writeJsonResponse(errorResponse);
        }
    }
    
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Server stopped";
    return 0;
}