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
echo "======================================"
echo "Summary:"
echo "======================================"
echo "C++ → C#: $([ $CPP_TO_CSHARP -eq 0 ] && echo "✅ PASSED" || echo "❌ FAILED")"
echo ""
echo "Note: C# → C++ test requires the C++ reader to wait for writers,"
echo "which the current test program doesn't support. This would require"
echo "modifying the C++ test reader to add a --wait-for-writer flag."