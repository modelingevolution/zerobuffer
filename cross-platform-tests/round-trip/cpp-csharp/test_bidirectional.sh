#!/bin/bash

# Bidirectional Round-Trip Tests
# Runs both C++ → C# and C# → C++ tests

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "======================================"
echo "ZeroBuffer Bidirectional Tests"
echo "======================================"
echo ""

# Test 1: C++ → C#
echo "Test 1: C++ → C# Round-Trip"
echo "-----------------------------------"
"$SCRIPT_DIR/test_cpp_to_csharp.sh"
CPP_TO_CSHARP=$?

if [ $CPP_TO_CSHARP -eq 0 ]; then
    echo "✅ C++ → C# test PASSED"
else
    echo "❌ C++ → C# test FAILED"
fi

echo ""

# Test 2: C# → C++
echo "Test 2: C# → C++ Round-Trip"
echo "-----------------------------------"
"$SCRIPT_DIR/test_csharp_to_cpp.sh"
CSHARP_TO_CPP=$?

if [ $CSHARP_TO_CPP -eq 0 ]; then
    echo "✅ C# → C++ test PASSED"
else
    echo "❌ C# → C++ test FAILED"
fi

echo ""
echo "======================================"
echo "Summary:"
echo "======================================"
echo "C++ → C#: $([ $CPP_TO_CSHARP -eq 0 ] && echo "✅ PASSED" || echo "❌ FAILED")"
echo "C# → C++: $([ $CSHARP_TO_CPP -eq 0 ] && echo "✅ PASSED" || echo "❌ FAILED")"

if [ $CPP_TO_CSHARP -eq 0 ] && [ $CSHARP_TO_CPP -eq 0 ]; then
    echo ""
    echo "✅ All tests PASSED!"
    exit 0
else
    echo ""
    echo "❌ Some tests FAILED"
    exit 1
fi