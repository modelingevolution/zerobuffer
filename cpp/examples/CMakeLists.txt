cmake_minimum_required(VERSION 3.20)

# Simple reader example
add_executable(example_reader
    example_reader.cpp
)

target_link_libraries(example_reader
    PRIVATE
        zerobuffer
)

# Simple writer example
add_executable(example_writer
    example_writer.cpp
)

target_link_libraries(example_writer
    PRIVATE
        zerobuffer
)

# Metadata example
add_executable(example_metadata
    example_metadata.cpp
)

target_link_libraries(example_metadata
    PRIVATE
        zerobuffer
)