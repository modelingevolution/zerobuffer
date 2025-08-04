cmake_minimum_required(VERSION 3.20)

# Create custom test main library with logging
add_library(test_main STATIC test_main.cpp)
target_link_libraries(test_main
    PUBLIC
        zerobuffer
        GTest::gtest
)

# Main test executable using Google Test
add_executable(test_zerobuffer
    test_zerobuffer.cpp
)

target_link_libraries(test_zerobuffer
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

# Comprehensive test scenarios
add_executable(test_scenarios
    test_scenarios.cpp
)

# Resource cleanup tests
add_executable(test_resource_cleanup
    test_resource_cleanup.cpp
)

# Debug test
add_executable(test_debug_buffer_full
    test_debug_buffer_full.cpp
)

# Zero-copy tests
add_executable(test_zerocopy
    test_zerocopy.cpp
)

# Free space accounting tests
add_executable(test_free_space_accounting
    test_free_space_accounting.cpp
)

# Debug test for free space
add_executable(test_free_space_debug
    test_free_space_debug.cpp
)

# Debug test for wrap scenario
add_executable(test_debug_wrap
    test_debug_wrap.cpp
)

# Minimal debug test
add_executable(test_minimal_debug
    test_minimal_debug.cpp
)

target_link_libraries(test_scenarios
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_resource_cleanup
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_debug_buffer_full
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_zerocopy
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_free_space_accounting
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_free_space_debug
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_debug_wrap
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

target_link_libraries(test_minimal_debug
    PRIVATE
        zerobuffer
        test_main
        GTest::gtest
)

# Add test discovery with reasonable timeouts
include(GoogleTest)
gtest_discover_tests(test_zerobuffer
    PROPERTIES TIMEOUT 30
)
gtest_discover_tests(test_scenarios
    PROPERTIES TIMEOUT 60
)
gtest_discover_tests(test_resource_cleanup
    PROPERTIES TIMEOUT 30
)
gtest_discover_tests(test_zerocopy
    PROPERTIES TIMEOUT 30
)
gtest_discover_tests(test_free_space_accounting
    PROPERTIES TIMEOUT 60
)

# Keep the example programs for manual testing
add_executable(test_multiprocess_reader
    test_multiprocess_reader.cpp
)

target_link_libraries(test_multiprocess_reader
    PRIVATE
        zerobuffer
)

add_executable(test_multiprocess_writer
    test_multiprocess_writer.cpp
)

target_link_libraries(test_multiprocess_writer
    PRIVATE
        zerobuffer
)

# Cross-platform test applications
option(BUILD_CROSS_PLATFORM_TESTS "Build cross-platform test applications" ON)
if(BUILD_CROSS_PLATFORM_TESTS)
    # Find JSON library (nlohmann/json)
    find_package(nlohmann_json QUIET)
    
    if(NOT nlohmann_json_FOUND)
        # Try to find it manually or use FetchContent
        include(FetchContent)
        FetchContent_Declare(
            json
            GIT_REPOSITORY https://github.com/nlohmann/json.git
            GIT_TAG v3.11.2
        )
        FetchContent_MakeAvailable(json)
    endif()
    
    add_executable(zerobuffer-test-writer
        cross-platform/test_writer.cpp
    )
    target_link_libraries(zerobuffer-test-writer
        PRIVATE
            zerobuffer
            nlohmann_json::nlohmann_json
    )
    
    add_executable(zerobuffer-test-reader
        cross-platform/test_reader.cpp
    )
    target_link_libraries(zerobuffer-test-reader
        PRIVATE
            zerobuffer
            nlohmann_json::nlohmann_json
    )
    
    # Install cross-platform test executables
    install(TARGETS zerobuffer-test-writer zerobuffer-test-reader
        RUNTIME DESTINATION bin
    )
endif()

