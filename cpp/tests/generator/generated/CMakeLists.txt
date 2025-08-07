# Generated tests from feature files
cmake_minimum_required(VERSION 3.20)

# Find the generated test files
file(GLOB GENERATED_TEST_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp")

if(GENERATED_TEST_SOURCES)
    # Create test executable for generated tests
    add_executable(zerobuffer_generated_tests
        ${GENERATED_TEST_SOURCES}
    )

    # Link with required libraries
    target_link_libraries(zerobuffer_generated_tests
        PRIVATE
        zerobuffer
        step_definitions  # From poc directory
        GTest::gtest
        GTest::gtest_main
        Threads::Threads
    )

    # Include directories
    target_include_directories(zerobuffer_generated_tests
        PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../..  # For step_definitions headers
    )

    # Set C++ standard
    target_compile_features(zerobuffer_generated_tests PRIVATE cxx_std_20)

    # Add tests to CTest
    include(GoogleTest)
    gtest_discover_tests(zerobuffer_generated_tests
        PROPERTIES TIMEOUT 30
    )
else()
    message(WARNING "No generated test files found in ${CMAKE_CURRENT_SOURCE_DIR}")
    message(WARNING "Run the test generator first: dotnet run --project tests/generator -- --input ../../ZeroBuffer.Harmony.Tests/Features --output generated")
endif()