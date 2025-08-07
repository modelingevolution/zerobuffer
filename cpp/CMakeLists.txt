cmake_minimum_required(VERSION 3.20)
project(zerobuffer VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find Boost for logging
find_package(Boost REQUIRED COMPONENTS log log_setup thread filesystem system)

# Platform detection
if(WIN32)
    set(PLATFORM_SOURCES src/platform_windows.cpp)
    set(PLATFORM_LIBS)
elseif(UNIX)
    set(PLATFORM_SOURCES src/platform_linux.cpp)
    set(PLATFORM_LIBS pthread rt)
else()
    message(FATAL_ERROR "Unsupported platform")
endif()

# Library sources
add_library(zerobuffer STATIC
    src/reader.cpp
    src/writer.cpp
    src/duplex_client.cpp
    src/immutable_duplex_server.cpp
    src/mutable_duplex_server.cpp
    src/duplex_channel_factory.cpp
    ${PLATFORM_SOURCES}
)

# Include directories
target_include_directories(zerobuffer
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${Boost_INCLUDE_DIRS}
)

# Link libraries
target_link_libraries(zerobuffer
    PUBLIC
        ${PLATFORM_LIBS}
        Boost::log
        Boost::log_setup
        Boost::thread
        Boost::filesystem
        Boost::system
)

# Compiler features
target_compile_features(zerobuffer PUBLIC cxx_std_20)

# Installation rules
install(TARGETS zerobuffer
    EXPORT zerobufferTargets
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
    RUNTIME DESTINATION bin
    INCLUDES DESTINATION include
)

install(DIRECTORY include/
    DESTINATION include
)

# Export targets
install(EXPORT zerobufferTargets
    FILE zerobufferTargets.cmake
    NAMESPACE zerobuffer::
    DESTINATION lib/cmake/zerobuffer
)

# Create package configuration files
include(CMakePackageConfigHelpers)
write_basic_package_version_file(
    "zerobufferConfigVersion.cmake"
    VERSION ${PROJECT_VERSION}
    COMPATIBILITY AnyNewerVersion
)

configure_package_config_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/cmake/zerobufferConfig.cmake.in"
    "${CMAKE_CURRENT_BINARY_DIR}/zerobufferConfig.cmake"
    INSTALL_DESTINATION lib/cmake/zerobuffer
)

install(FILES
    "${CMAKE_CURRENT_BINARY_DIR}/zerobufferConfig.cmake"
    "${CMAKE_CURRENT_BINARY_DIR}/zerobufferConfigVersion.cmake"
    DESTINATION lib/cmake/zerobuffer
)

# Tests
option(BUILD_TESTS "Build unit tests" ON)
if(BUILD_TESTS)
    enable_testing()
    
    # Try to find Google Test
    find_package(GTest QUIET)
    
    if(NOT GTest_FOUND)
        # Download and build Google Test
        include(FetchContent)
        FetchContent_Declare(
            googletest
            GIT_REPOSITORY https://github.com/google/googletest.git
            GIT_TAG release-1.12.1
        )
        FetchContent_MakeAvailable(googletest)
    endif()
    
    add_subdirectory(tests)
endif()

# Examples
option(BUILD_EXAMPLES "Build examples" ON)
if(BUILD_EXAMPLES)
    add_subdirectory(examples)
endif()

# Benchmarks
option(BUILD_BENCHMARKS "Build benchmarks" ON)
if(BUILD_BENCHMARKS)
    add_subdirectory(benchmarks)
endif()

# Serve (JSON-RPC server for Harmony)
option(BUILD_SERVE "Build zerobuffer-serve for Harmony integration" ON)
if(BUILD_SERVE)
    # Ensure nlohmann_json is available
    find_package(nlohmann_json QUIET)
    if(NOT nlohmann_json_FOUND)
        include(FetchContent)
        FetchContent_Declare(
            json
            GIT_REPOSITORY https://github.com/nlohmann/json.git
            GIT_TAG v3.11.2
        )
        FetchContent_MakeAvailable(json)
    endif()
    
    add_subdirectory(serve)
endif()