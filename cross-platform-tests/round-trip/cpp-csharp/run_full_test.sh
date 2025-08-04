#!/bin/bash

# Run all round-trip tests

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "======================================"
echo "ZeroBuffer Round-Trip Tests"
echo "======================================"
echo ""

# Test 1: C++ → C#
echo "Test 1: C++ → C# Round-Trip"
echo "-----------------------------------"
"$SCRIPT_DIR/test_cpp_to_csharp.sh"
CPP_TO_CSHARP=$?
echo ""

# Test 2: C# → C++
echo "Test 2: C# → C++ Round-Trip"
echo "-----------------------------------"
"$SCRIPT_DIR/test_csharp_to_cpp.sh"
CSHARP_TO_CPP=$?
echo ""

# Summary
echo "======================================"
echo "Summary:"
echo "======================================"
echo "C++ → C#: $([ $CPP_TO_CSHARP -eq 0 ] && echo "✓ PASSED" || echo "✗ FAILED")"
echo "C# → C++: $([ $CSHARP_TO_CPP -eq 0 ] && echo "✓ PASSED" || echo "✗ FAILED")"
echo ""

if [ $CPP_TO_CSHARP -eq 0 ] && [ $CSHARP_TO_CPP -eq 0 ]; then
    echo "✓ All tests passed!"
    exit 0
else
    echo "✗ Some tests failed"
    exit 1
fi