#!/bin/bash

# Unified Cross-Platform Test Runner
# Uses the standardized test application interface

set -e

# Test configuration
FRAMES=1000
FRAME_SIZE=1024
BUFFER_SIZE=$((10 * 1024 * 1024))
METADATA_SIZE=1024
TIMEOUT_MS=5000

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Get test executables based on language
get_executable() {
    local lang=$1
    local app=$2
    
    case "$lang" in
        "cpp")
            echo "../cpp/build/tests/cross-platform/zerobuffer-test-$app"
            ;;
        "csharp")
            echo "dotnet run --project ../csharp/ZeroBuffer.CrossPlatform -- $app"
            ;;
        "python")
            echo "python3 -m zerobuffer.cross_platform.$app"
            ;;
        *)
            echo "Unknown language: $lang" >&2
            exit 1
            ;;
    esac
}

# Run round-trip test
run_round_trip() {
    local writer_lang=$1
    local reader_lang=$2
    local test_name="${writer_lang}_to_${reader_lang}"
    local buffer_name="test_${test_name}_$$"
    
    echo -e "${BLUE}Running round-trip test: ${writer_lang} → ${reader_lang}${NC}"
    
    # Get executables
    local writer_cmd=$(get_executable "$writer_lang" "writer")
    local reader_cmd=$(get_executable "$reader_lang" "reader")
    
    # Start reader
    echo "Starting $reader_lang reader..."
    $reader_cmd "$buffer_name" \
        --create \
        --buffer-size $BUFFER_SIZE \
        --metadata-size $METADATA_SIZE \
        --frames $FRAMES \
        --validate \
        --check-pattern \
        --timeout-ms $TIMEOUT_MS \
        --json-output > "results/${test_name}_reader.json" 2>&1 &
    local reader_pid=$!
    
    # Give reader time to create buffer
    sleep 2
    
    # Run writer
    echo "Starting $writer_lang writer..."
    $writer_cmd "$buffer_name" \
        --frames $FRAMES \
        --size $FRAME_SIZE \
        --metadata "Test metadata from $writer_lang" \
        --pattern sequential \
        --json-output > "results/${test_name}_writer.json" 2>&1
    
    # Wait for reader
    if wait $reader_pid; then
        echo -e "${GREEN}✓ Test passed${NC}"
        parse_results "results/${test_name}_writer.json" "results/${test_name}_reader.json"
        return 0
    else
        echo -e "${RED}✗ Test failed${NC}"
        return 1
    fi
}

# Run relay test
run_relay_test() {
    local writer_lang=$1
    local relay_lang=$2
    local reader_lang=$3
    local test_name="${writer_lang}_${relay_lang}_${reader_lang}"
    local buffer1="relay_in_${test_name}_$$"
    local buffer2="relay_out_${test_name}_$$"
    
    echo -e "${BLUE}Running relay test: ${writer_lang} → ${relay_lang} → ${reader_lang}${NC}"
    
    # Get executables
    local writer_cmd=$(get_executable "$writer_lang" "writer")
    local relay_cmd=$(get_executable "$relay_lang" "relay")
    local reader_cmd=$(get_executable "$reader_lang" "reader")
    
    # Start reader
    echo "Starting $reader_lang reader..."
    $reader_cmd "$buffer2" \
        --create \
        --buffer-size $BUFFER_SIZE \
        --metadata-size $METADATA_SIZE \
        --frames $FRAMES \
        --validate \
        --timeout-ms $TIMEOUT_MS \
        --json-output > "results/${test_name}_reader.json" 2>&1 &
    local reader_pid=$!
    
    sleep 1
    
    # Start relay
    echo "Starting $relay_lang relay..."
    $relay_cmd "$buffer1" "$buffer2" \
        --frames $FRAMES \
        --create-output \
        --timeout-ms $TIMEOUT_MS \
        --json-output > "results/${test_name}_relay.json" 2>&1 &
    local relay_pid=$!
    
    sleep 1
    
    # Run writer
    echo "Starting $writer_lang writer..."
    $writer_cmd "$buffer1" \
        --frames $FRAMES \
        --size $FRAME_SIZE \
        --pattern sequential \
        --json-output > "results/${test_name}_writer.json" 2>&1
    
    # Wait for reader
    if wait $reader_pid; then
        echo -e "${GREEN}✓ Test passed${NC}"
        kill $relay_pid 2>/dev/null || true
        return 0
    else
        echo -e "${RED}✗ Test failed${NC}"
        kill $relay_pid 2>/dev/null || true
        return 1
    fi
}

# Parse and display results
parse_results() {
    local writer_json=$1
    local reader_json=$2
    
    if command -v jq >/dev/null 2>&1; then
        local writer_throughput=$(jq -r '.throughput_mbps // "N/A"' "$writer_json" 2>/dev/null || echo "N/A")
        local reader_throughput=$(jq -r '.throughput_mbps // "N/A"' "$reader_json" 2>/dev/null || echo "N/A")
        local reader_latency=$(jq -r '.average_latency_us // "N/A"' "$reader_json" 2>/dev/null || echo "N/A")
        
        echo "  Writer throughput: $writer_throughput MB/s"
        echo "  Reader throughput: $reader_throughput MB/s"
        echo "  Average latency: $reader_latency μs"
    fi
}

# Main test execution
main() {
    echo "========================================="
    echo "Unified Cross-Platform Test Runner"
    echo "========================================="
    echo ""
    echo "Configuration:"
    echo "  Frames: $FRAMES"
    echo "  Frame size: $FRAME_SIZE bytes"
    echo "  Buffer size: $((BUFFER_SIZE / 1024 / 1024)) MB"
    echo ""
    
    # Create results directory
    mkdir -p results
    
    # Languages to test
    local languages=("cpp" "csharp" "python")
    
    # Run round-trip tests for all combinations
    echo -e "${YELLOW}Round-Trip Tests${NC}"
    echo "================="
    
    for writer in "${languages[@]}"; do
        for reader in "${languages[@]}"; do
            if [ "$writer" != "$reader" ]; then
                run_round_trip "$writer" "$reader" || true
                echo ""
            fi
        done
    done
    
    # Run relay tests (subset of combinations)
    echo -e "${YELLOW}Relay Tests${NC}"
    echo "==========="
    
    # Test each language as relay
    for relay in "${languages[@]}"; do
        # Pick different writer and reader
        case "$relay" in
            "cpp")
                run_relay_test "csharp" "cpp" "python" || true
                ;;
            "csharp")
                run_relay_test "python" "csharp" "cpp" || true
                ;;
            "python")
                run_relay_test "cpp" "python" "csharp" || true
                ;;
        esac
        echo ""
    done
    
    echo "========================================="
    echo "Test run complete!"
    echo "Results saved in: results/"
    echo "========================================="
}

# Run tests
main "$@"