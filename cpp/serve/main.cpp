/**
 * zerobuffer-serve - JSON-RPC server for Harmony test orchestration
 * 
 * Iteration 3: Minimal JSON-RPC server that can execute steps
 * - Reads JSON from stdin (line by line for now, no Content-Length headers yet)
 * - Handles executeStep method
 * - Returns success/failure as JSON
 */

#include "step_definitions/step_registry.h"
#include "step_definitions/test_context.h"
#include "step_definitions/basic_communication_steps.h"

#include <zerobuffer/logger.h>
#include <nlohmann/json.hpp>
#include <iostream>
#include <string>
#include <exception>

using json = nlohmann::json;
using namespace zerobuffer::steps;

// Global test context (will be managed better in iteration 4)
static TestContext g_testContext;

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
            json params = request.value("params", json::object());
            std::string stepText = params.value("step", "");
            
            if (stepText.empty()) {
                response["error"] = {
                    {"code", -32602},
                    {"message", "Invalid params: missing 'step' field"}
                };
                return response;
            }
            
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Executing step: " << stepText;
            
            // Execute the step
            bool success = StepRegistry::getInstance().executeStep(stepText, g_testContext);
            
            // Build result
            json result = {
                {"success", success},
                {"data", json::object()},
                {"logs", json::array()}
            };
            
            if (!success) {
                result["error"] = "Step execution failed";
            } else {
                result["error"] = json();  // null value
            }
            
            response["result"] = result;
            
        } else if (method == "health") {
            // Simple health check
            response["result"] = true;
            
        } else if (method == "initialize") {
            // Initialize test context (minimal for now)
            g_testContext.reset();
            ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Test context initialized";
            response["result"] = true;
            
        } else if (method == "discover") {
            // Return list of registered steps
            auto steps = StepRegistry::getInstance().getAllSteps();
            json stepList = json::array();
            
            for (const auto& step : steps) {
                stepList.push_back(step.pattern);
            }
            
            response["result"] = {
                {"steps", stepList}
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
    // Initialize logging
    zerobuffer::init_logging(zerobuffer::info);
    
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Starting JSON-RPC server (Iteration 3)";
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Reading from stdin, writing to stdout";
    
    // Register step definitions
    registerBasicCommunicationSteps();
    
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Step definitions registered";
    
    // Main loop - read JSON lines from stdin
    std::string line;
    while (std::getline(std::cin, line)) {
        if (line.empty()) continue;
        
        try {
            // Parse JSON request
            json request = json::parse(line);
            
            // Handle the request
            json response = handleRequest(request);
            
            // Send response to stdout
            std::cout << response.dump() << std::endl;
            
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
            std::cout << errorResponse.dump() << std::endl;
        }
    }
    
    ZEROBUFFER_LOG_INFO("zerobuffer-serve") << "Server stopped";
    return 0;
}