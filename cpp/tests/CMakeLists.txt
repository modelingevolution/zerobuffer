cmake_minimum_required(VERSION 3.20)

# Main test executable using Google Test
add_executable(test_zerobuffer
    test_zerobuffer.cpp
)

target_link_libraries(test_zerobuffer
    PRIVATE
        zerobuffer
        GTest::gtest_main
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
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_resource_cleanup
    PRIVATE
        zerobuffer
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_debug_buffer_full
    PRIVATE
        zerobuffer
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_zerocopy
    PRIVATE
        zerobuffer
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_free_space_accounting
    PRIVATE
        zerobuffer
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_free_space_debug
    PRIVATE
        zerobuffer
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_debug_wrap
    PRIVATE
        zerobuffer
        GTest::gtest_main
        GTest::gtest
)

target_link_libraries(test_minimal_debug
    PRIVATE
        zerobuffer
        GTest::gtest_main
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