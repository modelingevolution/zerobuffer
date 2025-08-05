#!/bin/bash
cd /mnt/d/source/modelingevolution/streamer/src/zerobuffer/ZeroBuffer.Harmony.Tests
echo "Running one csharp/csharp test..."
dotnet test --no-build --filter "FullyQualifiedName~ZeroBufferE2ETests.RunScenario" -v n 2>&1 | head -300 | grep -E "(Failed|Passed|csharp/csharp.*Test 1\.1|Frame data validated|Error Message:|Scenario failed)" -A3