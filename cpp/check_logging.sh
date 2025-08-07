#!/bin/bash

# Script to check for usage of std::cout or std::cerr in test code
# These should use ZEROBUFFER_LOG_* macros instead

echo "Checking for std::cout or std::cerr usage in test code..."

# Files to check (excluding main.cpp since it may need std::cout for JSON output)
FILES_TO_CHECK=(
    "step_definitions/*.cpp"
    "step_definitions/*.h"
    "tests/*.cpp"
    "tests/poc/*.cpp"
)

FOUND_ISSUES=0

for pattern in "${FILES_TO_CHECK[@]}"; do
    for file in $pattern; do
        if [ -f "$file" ]; then
            # Check for std::cout
            if grep -q "std::cout" "$file"; then
                echo "❌ Found std::cout in $file"
                grep -n "std::cout" "$file" | head -3
                FOUND_ISSUES=1
            fi
            
            # Check for std::cerr
            if grep -q "std::cerr" "$file"; then
                echo "❌ Found std::cerr in $file"
                grep -n "std::cerr" "$file" | head -3
                FOUND_ISSUES=1
            fi
        fi
    done
done

# Special case: serve/main.cpp should only use std::cout for JSON output
if [ -f "serve/main.cpp" ]; then
    # Check for std::cerr (should not be used)
    if grep -q "std::cerr" "serve/main.cpp"; then
        echo "❌ Found std::cerr in serve/main.cpp (should use ZEROBUFFER_LOG_*)"
        grep -n "std::cerr" "serve/main.cpp" | head -3
        FOUND_ISSUES=1
    fi
    
    # Check that std::cout is only used for JSON output
    NON_JSON_COUT=$(grep "std::cout" "serve/main.cpp" | grep -v "response.dump()" | grep -v "errorResponse.dump()")
    if [ ! -z "$NON_JSON_COUT" ]; then
        echo "❌ Found non-JSON std::cout usage in serve/main.cpp"
        echo "$NON_JSON_COUT" | head -3
        FOUND_ISSUES=1
    fi
fi

if [ $FOUND_ISSUES -eq 0 ]; then
    echo "✅ No logging issues found - all test code uses proper logging"
else
    echo ""
    echo "⚠️  Please replace std::cout/std::cerr with ZEROBUFFER_LOG_* macros:"
    echo "    ZEROBUFFER_LOG_TRACE(channel)   - for trace messages"
    echo "    ZEROBUFFER_LOG_DEBUG(channel)   - for debug messages"
    echo "    ZEROBUFFER_LOG_INFO(channel)    - for info messages"
    echo "    ZEROBUFFER_LOG_WARNING(channel) - for warnings"
    echo "    ZEROBUFFER_LOG_ERROR(channel)   - for errors"
    echo ""
    echo "Example:"
    echo '    ZEROBUFFER_LOG_INFO("MyModule") << "Processing " << count << " items";'
    exit 1
fi

exit 0