cmake_minimum_required(VERSION 3.20)

# POC tests for testing infrastructure development
# DISABLED: These are proof-of-concept tests, not needed for production
# add_subdirectory(poc)

# Duplex channel tests
# DISABLED: Not needed for Harmony integration testing
# add_executable(test_duplex_channel
#     test_duplex_channel.cpp
# )
# 
# target_link_libraries(test_duplex_channel
#     PRIVATE
#         zerobuffer
#         GTest::gtest
#         GTest::gtest_main
# )
# 
# # Add test discovery
# include(GoogleTest)
# gtest_discover_tests(test_duplex_channel
#     PROPERTIES TIMEOUT 30
# )
# 
# # Simple duplex test
# add_executable(test_duplex_simple
#     test_duplex_simple.cpp
# )
# 
# target_link_libraries(test_duplex_simple
#     PRIVATE
#         zerobuffer
# )

# Frame RAII tests
add_executable(test_frame_raii
    test_frame_raii.cpp
)

target_link_libraries(test_frame_raii
    PRIVATE
        zerobuffer
        GTest::gtest
        GTest::gtest_main
)

# Add test discovery
include(GoogleTest)
gtest_discover_tests(test_frame_raii
    PROPERTIES TIMEOUT 30
)

# Generated tests from feature files
if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/generated/CMakeLists.txt")
    add_subdirectory(generated)
    message(STATUS "Found generated tests - including them in build")
else()
    message(WARNING "No generated tests found. Run the generator first:")
    message(WARNING "  harmony-cpp-gen --input ../ZeroBuffer.Harmony.Tests/Features --output tests/generated")
    message(WARNING "  Or run: ./generate_tests.sh")
endif()

# Cross-platform test applications - only include if they exist
if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/cross-platform/test_writer.cpp" AND 
   EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/cross-platform/test_reader.cpp")
    
    option(BUILD_CROSS_PLATFORM_TESTS "Build cross-platform test applications" ON)
    if(BUILD_CROSS_PLATFORM_TESTS)
        # Find JSON library (nlohmann/json)
        find_package(nlohmann_json QUIET)
        
        if(NOT nlohmann_json_FOUND)
            # Try to find it manually or use FetchContent
            include(FetchContent)
            
            # Set CMake policy to suppress deprecation warnings from fetched content
            set(CMAKE_POLICY_DEFAULT_CMP0048 NEW)
            cmake_policy(SET CMP0048 NEW)
            
            FetchContent_Declare(
                json
                GIT_REPOSITORY https://github.com/nlohmann/json.git
                GIT_TAG v3.11.3  # Updated to latest stable version
                CMAKE_ARGS
                    -DCMAKE_POLICY_DEFAULT_CMP0048=NEW
            )
            
            # Suppress warnings from the fetched content
            set(JSON_BuildTests OFF CACHE INTERNAL "")
            set(JSON_Install OFF CACHE INTERNAL "")
            
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
endif()