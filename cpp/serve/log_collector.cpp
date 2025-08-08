#include "log_collector.h"
#include <boost/log/trivial.hpp>
#include <boost/log/attributes/value_extraction.hpp>
#include <boost/log/expressions.hpp>
#include <boost/log/utility/formatting_ostream.hpp>
#include <boost/log/attributes/named_scope.hpp>
#include <boost/date_time/posix_time/posix_time.hpp>
#include <sstream>

namespace zerobuffer {
namespace serve {

namespace logging = boost::log;
namespace attrs = boost::log::attributes;
namespace expr = boost::log::expressions;

void LogCollectorBackend::consume(logging::record_view const& rec) {
    if (!collecting_) {
        return;
    }
    
    // Extract severity level using Boost's trivial severity
    auto severity = rec[logging::trivial::severity];
    std::string level = "INFO";
    
    if (severity) {
        switch (*severity) {
            case logging::trivial::trace:
                level = "TRACE";
                break;
            case logging::trivial::debug:
                level = "DEBUG";
                break;
            case logging::trivial::info:
                level = "INFO";
                break;
            case logging::trivial::warning:
                level = "WARNING";
                break;
            case logging::trivial::error:
                level = "ERROR";
                break;
            case logging::trivial::fatal:
                level = "FATAL";
                break;
        }
    }
    
    // Extract message
    auto message_attr = rec[expr::smessage];
    std::string message;
    if (message_attr) {
        message = *message_attr;
    }
    
    // Extract component name if available
    auto component_attr = logging::extract<std::string>("Component", rec);
    if (component_attr) {
        message = "[" + *component_attr + "] " + message;
    }
    
    // Add to collection
    std::lock_guard<std::mutex> lock(mutex_);
    logs_.push_back({level, message});
}

std::vector<LogEntry> LogCollectorBackend::get_and_clear_logs() {
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<LogEntry> result = std::move(logs_);
    logs_.clear();
    return result;
}

void LogCollectorBackend::clear_logs() {
    std::lock_guard<std::mutex> lock(mutex_);
    logs_.clear();
}

void LogCollectorBackend::set_collecting(bool collecting) {
    std::lock_guard<std::mutex> lock(mutex_);
    collecting_ = collecting;
}

LogCollector::LogCollector() {
    // Create the backend
    backend_ = boost::make_shared<LogCollectorBackend>();
    
    // Create the sink with the backend
    sink_ = boost::make_shared<logging::sinks::synchronous_sink<LogCollectorBackend>>(backend_);
    
    // Add the sink to the logging core
    logging::core::get()->add_sink(sink_);
}

LogCollector::~LogCollector() {
    // Remove the sink from the logging core
    logging::core::get()->remove_sink(sink_);
}

void LogCollector::start_collecting() {
    backend_->set_collecting(true);
}

void LogCollector::stop_collecting() {
    backend_->set_collecting(false);
}

std::vector<LogEntry> LogCollector::get_and_clear_logs() {
    return backend_->get_and_clear_logs();
}

void LogCollector::clear_logs() {
    backend_->clear_logs();
}

json LogCollector::get_logs_as_json() {
    auto logs = get_and_clear_logs();
    json result = json::array();
    
    for (const auto& log : logs) {
        result.push_back(log.to_json());
    }
    
    return result;
}

// Global instance
LogCollector& get_log_collector() {
    static LogCollector collector;
    return collector;
}

} // namespace serve
} // namespace zerobuffer