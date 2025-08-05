#!/bin/bash
cd /mnt/d/source/modelingevolution/streamer/src/zerobuffer/ZeroBuffer.Harmony.Tests
echo "Running single test..."
dotnet test --no-build --filter "DisplayName~csharp/csharp.*Test 1.1" -v n 2>&1 | grep -E "(Test 1\.1|Passed|Failed|Frame data validated|No frame|signals space available)" | head -20
echo "Test completed"