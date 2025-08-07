#ifndef ZEROBUFFER_LOGGER_H
#define ZEROBUFFER_LOGGER_H

#include <boost/log/trivial.hpp>
#include <boost/log/sources/severity_logger.hpp>
#include <boost/log/utility/setup/console.hpp>
#include <boost/log/utility/setup/common_attributes.hpp>
#include <boost/log/expressions.hpp>
#include <boost/log/support/date_time.hpp>
#include <string>

namespace zerobuffer {

namespace logging = boost::log;
namespace keywords = boost::log::keywords;
namespace expr = boost::log::expressions;

// Severity levels
enum severity_level {
    trace,
    debug,
    info,
    warning,
    error,
    fatal
};

// Convert severity level to string
inline std::ostream& operator<<(std::ostream& strm, severity_level level) {
    static const char* levels[] = {
        "TRACE",
        "DEBUG",
        "INFO",
        "WARNING",
        "ERROR",
        "FATAL"
    };
    
    if (static_cast<std::size_t>(level) < sizeof(levels) / sizeof(*levels))
        strm << levels[level];
    else
        strm << static_cast<int>(level);
    
    return strm;
}

// Logger type
using Logger = logging::sources::severity_logger<severity_level>;

// Initialize logging system
inline void init_logging(severity_level min_level = info) {
    // Add common attributes
    logging::add_common_attributes();
    
    // Setup console sink with formatting (write to stderr)
    auto console = logging::add_console_log(
        std::cerr,
        keywords::format = expr::stream
            << "[" << expr::format_date_time<boost::posix_time::ptime>("TimeStamp", "%Y-%m-%d %H:%M:%S.%f")
            << "] [" << expr::attr<severity_level>("Severity")
            << "] " << expr::smessage
    );
    
    // Set minimum severity level
    logging::core::get()->set_filter(
        expr::attr<severity_level>("Severity") >= min_level
    );
}

// Convenience macros for logging
#define ZEROBUFFER_LOG_TRACE(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), zerobuffer::trace) << "[" << channel << "] "

#define ZEROBUFFER_LOG_DEBUG(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), zerobuffer::debug) << "[" << channel << "] "

#define ZEROBUFFER_LOG_INFO(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), zerobuffer::info) << "[" << channel << "] "

#define ZEROBUFFER_LOG_WARNING(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), zerobuffer::warning) << "[" << channel << "] "

#define ZEROBUFFER_LOG_ERROR(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), zerobuffer::error) << "[" << channel << "] "

#define ZEROBUFFER_LOG_FATAL(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), zerobuffer::fatal) << "[" << channel << "] "

// Global logger instance
inline Logger& _get_logger() {
    static Logger instance;
    return instance;
}

} // namespace zerobuffer

#endif // ZEROBUFFER_LOGGER_H