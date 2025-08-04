cmake_minimum_required(VERSION 3.20)
project(test_zerobuffer_registry)

find_package(zerobuffer CONFIG REQUIRED)

add_executable(test_app main.cpp)
target_link_libraries(test_app PRIVATE zerobuffer::zerobuffer)
