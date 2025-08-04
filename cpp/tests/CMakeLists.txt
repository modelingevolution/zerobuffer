cmake_minimum_required(VERSION 3.20)

# Duplex channel tests
add_executable(test_duplex_channel
    test_duplex_channel.cpp
)

target_link_libraries(test_duplex_channel
    PRIVATE
        zerobuffer
        GTest::gtest
        GTest::gtest_main
)

# Add test discovery
include(GoogleTest)
gtest_discover_tests(test_duplex_channel
    PROPERTIES TIMEOUT 30
)

# Simple duplex test
add_executable(test_duplex_simple
    test_duplex_simple.cpp
)

target_link_libraries(test_duplex_simple
    PRIVATE
        zerobuffer
)

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
endif()