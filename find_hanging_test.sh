#!/bin/bash

# Script to run each C++ integration test individually to find which one hangs

cd /mnt/d/source/modelingevolution/streamer/src/zerobuffer/cpp/ZeroBuffer.Cpp.Integration.Tests

# Array of test names
tests=(
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.HealthCheck_ShouldReturnTrue"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.Initialize_ShouldAcceptValidParameters_AndReturnTrue"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.Discover_ShouldReturnStepDefinitions"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.ExecuteStep_InitializeEnvironment_ShouldSucceed"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.ExecuteStep_CreateBuffer_ShouldSucceed"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.ExecuteStep_CompleteTest11Scenario_ShouldSucceed"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.ExecuteStep_InvalidStep_ShouldReturnError"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.Cleanup_ShouldSucceed"
    "ZeroBuffer.Cpp.Integration.Tests.CppServeIntegrationTests.VerifyHarmonyStepFormat_ShouldIncludeFullText"
    "ZeroBuffer.Cpp.Integration.Tests.LoggingTests.ExecuteStep_ShouldReturnLogs"
    "ZeroBuffer.Cpp.Integration.Tests.LoggingTests.ExecuteStep_WithDebugLogging_ShouldReturnDebugLogs"
    "ZeroBuffer.Cpp.Integration.Tests.LoggingTests.ExecuteStep_WithError_ShouldReturnErrorLogs"
    "ZeroBuffer.Cpp.Integration.Tests.RawProtocolTests.SendRawHealthRequest_ShouldReceiveResponse"
    "ZeroBuffer.Cpp.Integration.Tests.RawProtocolTests.SendMultipleRequests_ShouldReceiveResponses"
)

echo "Running C++ integration tests individually to find hanging test..."
echo "============================================================"

# Counter for test number
count=1
total=${#tests[@]}

# Run each test individually
for test in "${tests[@]}"; do
    echo ""
    echo "[$count/$total] Running: $test"
    echo "------------------------------------------------------------"
    
    # Run test with 30 second timeout
    timeout 30 dotnet test --filter "FullyQualifiedName=$test" --logger "console;verbosity=minimal" --verbosity minimal 2>&1
    
    exit_code=$?
    
    if [ $exit_code -eq 124 ]; then
        echo "❌ TEST TIMED OUT AFTER 30 SECONDS: $test"
        echo "============================================================"
        echo "FOUND HANGING TEST: $test"
        echo "============================================================"
        break
    elif [ $exit_code -eq 0 ]; then
        echo "✅ Test passed"
    else
        echo "❌ Test failed with exit code: $exit_code"
    fi
    
    ((count++))
done

echo ""
echo "Test execution complete."