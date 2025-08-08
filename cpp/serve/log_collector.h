#ifndef ZEROBUFFER_SERVE_LOG_COLLECTOR_H
#define ZEROBUFFER_SERVE_LOG_COLLECTOR_H

#include <nlohmann/json.hpp>
#include <boost/log/sinks/basic_sink_backend.hpp>
#include <boost/log/sinks/sync_frontend.hpp>
#include <boost/log/core.hpp>
#include <boost/log/trivial.hpp>
#include <boost/smart_ptr/shared_ptr.hpp>
#include <boost/smart_ptr/make_shared_object.hpp>
#include <mutex>
#include <vector>
#include <string>

namespace zerobuffer {
namespace serve {

using json = nlohmann::json;

/**
 * Log entry structure matching C# LogEntry
 */
struct LogEntry {
    std::string level;
    std::string message;
    
    json to_json() const {
        return {
            {"Level", level},
            {"Message", message}
        };
    }
};

/**
 * Custom sink backend that collects log messages in memory
 */
class LogCollectorBackend : public boost::log::sinks::basic_sink_backend<
    boost::log::sinks::synchronized_feeding
> {
public:
    using string_type = std::string;
    
    LogCollectorBackend() = default;
    
    // Called by Boost.Log when a log record needs to be processed
    void consume(boost::log::record_view const& rec);
    
    // Get all collected logs and clear the collection
    std::vector<LogEntry> get_and_clear_logs();
    
    // Clear all logs without returning them
    void clear_logs();
    
    // Enable/disable log collection
    void set_collecting(bool collecting);
    
private:
    std::mutex mutex_;
    std::vector<LogEntry> logs_;
    bool collecting_ = true;
};

/**
 * Thread-safe log collector that can be attached to Boost.Log
 */
class LogCollector {
public:
    LogCollector();
    ~LogCollector();
    
    // Start collecting logs
    void start_collecting();
    
    // Stop collecting logs
    void stop_collecting();
    
    // Get all collected logs and clear the collection
    std::vector<LogEntry> get_and_clear_logs();
    
    // Clear all logs without returning them
    void clear_logs();
    
    // Convert logs to JSON array
    json get_logs_as_json();
    
private:
    boost::shared_ptr<LogCollectorBackend> backend_;
    boost::shared_ptr<boost::log::sinks::synchronous_sink<LogCollectorBackend>> sink_;
};

/**
 * Global log collector instance for the serve process
 */
LogCollector& get_log_collector();

} // namespace serve
} // namespace zerobuffer

#endif // ZEROBUFFER_SERVE_LOG_COLLECTOR_H