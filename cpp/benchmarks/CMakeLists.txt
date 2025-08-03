cmake_minimum_required(VERSION 3.20)

# Cross-process round-trip latency benchmark
add_executable(benchmark_roundtrip
    benchmark_roundtrip.cpp
)

target_link_libraries(benchmark_roundtrip
    PRIVATE
        zerobuffer
)

# Relay process for round-trip benchmark
add_executable(relay_process
    relay_process.cpp
)

target_link_libraries(relay_process
    PRIVATE
        zerobuffer
)

# Set runtime output directory so executables can find each other
set_target_properties(benchmark_roundtrip relay_process
    PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/benchmarks"
)