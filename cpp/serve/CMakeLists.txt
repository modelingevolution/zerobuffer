# ZeroBuffer serve - JSON-RPC server for Harmony test orchestration
cmake_minimum_required(VERSION 3.20)

# Create zerobuffer-serve executable (v2 with Content-Length support)
add_executable(zerobuffer-serve
    main_v2.cpp
    log_collector.cpp
    ../step_definitions/step_registry.cpp
    ../step_definitions/test_context.cpp
    ../step_definitions/basic_communication_steps.cpp
    ../step_definitions/buffer_naming_service.cpp
    ../step_definitions/test_data_patterns.cpp
    ../step_definitions/duplex_channel_steps.cpp
)

# Include directories
target_include_directories(zerobuffer-serve
    PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../
)

# Link libraries
target_link_libraries(zerobuffer-serve
    PRIVATE
    zerobuffer
    nlohmann_json::nlohmann_json
)

# Use C++20
target_compile_features(zerobuffer-serve PRIVATE cxx_std_20)

# Set output name
set_target_properties(zerobuffer-serve PROPERTIES
    OUTPUT_NAME "zerobuffer-serve"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/serve"
)