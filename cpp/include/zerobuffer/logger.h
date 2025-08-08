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
namespace trivial = boost::log::trivial;

// Use Boost's built-in severity levels
using severity_level = boost::log::trivial::severity_level;

// Logger type
using Logger = logging::sources::severity_logger<severity_level>;

// Initialize logging system
inline void init_logging(severity_level min_level = trivial::info) {
    // Add common attributes
    logging::add_common_attributes();
    
    // Setup console sink with formatting (write to stderr)
    auto console = logging::add_console_log(
        std::cerr,
        keywords::format = expr::stream
            << "[" << expr::format_date_time<boost::posix_time::ptime>("TimeStamp", "%Y-%m-%d %H:%M:%S.%f")
            << "] [" << trivial::severity
            << "] " << expr::smessage
    );
    
    // Set minimum severity level
    logging::core::get()->set_filter(
        trivial::severity >= min_level
    );
}

// Convenience macros for logging
#define ZEROBUFFER_LOG_TRACE(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), boost::log::trivial::trace) << "[" << channel << "] "

#define ZEROBUFFER_LOG_DEBUG(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), boost::log::trivial::debug) << "[" << channel << "] "

#define ZEROBUFFER_LOG_INFO(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), boost::log::trivial::info) << "[" << channel << "] "

#define ZEROBUFFER_LOG_WARNING(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), boost::log::trivial::warning) << "[" << channel << "] "

#define ZEROBUFFER_LOG_ERROR(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), boost::log::trivial::error) << "[" << channel << "] "

#define ZEROBUFFER_LOG_FATAL(channel) \
    BOOST_LOG_SEV(::zerobuffer::_get_logger(), boost::log::trivial::fatal) << "[" << channel << "] "

// Global logger instance
inline Logger& _get_logger() {
    static Logger instance;
    return instance;
}

} // namespace zerobuffer

#endif // ZEROBUFFER_LOGGER_H