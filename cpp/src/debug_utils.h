#ifndef DEBUG_UTILS_H
#define DEBUG_UTILS_H

#include <iostream>
#include <sstream>

// Uncomment to enable debug logging
// #define ZEROBUFFER_DEBUG

#ifdef ZEROBUFFER_DEBUG
#define DEBUG_LOG(msg) do { \
    std::cerr << "[DEBUG] " << msg << std::endl; \
} while(0)
#else
#define DEBUG_LOG(msg) do { } while(0)
#endif

#endif // DEBUG_UTILS_H