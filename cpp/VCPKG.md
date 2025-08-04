# Using ZeroBuffer with vcpkg

This guide explains how to use ZeroBuffer with vcpkg package manager.

## Installing ZeroBuffer via vcpkg

### Option 1: From Official vcpkg Repository (Coming Soon)

Once ZeroBuffer is accepted into the official vcpkg repository:

```bash
# Just install directly - no configuration needed!
vcpkg install zerobuffer
```

### Option 2: Using Custom Registry

Add to your `vcpkg-configuration.json`:

```json
{
  "registries": [
    {
      "kind": "git", 
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "latest-commit-sha",
      "packages": ["zerobuffer"]
    }
  ]
}
```

Then install:
```bash
vcpkg install zerobuffer
```

### Option 3: Using Local Port (Development)

If you have the ZeroBuffer source code:

```bash
# From your vcpkg root directory
vcpkg install zerobuffer --overlay-ports=/path/to/zerobuffer/vcpkg-port
```

## Using ZeroBuffer in your CMake project

### With vcpkg toolchain

```cmake
# In your CMakeLists.txt
find_package(zerobuffer CONFIG REQUIRED)
target_link_libraries(your_target PRIVATE zerobuffer::zerobuffer)
```

### Building your project

```bash
cmake -B build -S . -DCMAKE_TOOLCHAIN_FILE=[path to vcpkg]/scripts/buildsystems/vcpkg.cmake
cmake --build build
```

## Consuming ZeroBuffer in your project

### Using vcpkg.json manifest

Create a `vcpkg.json` in your project root:

```json
{
  "name": "your-project",
  "version": "1.0.0",
  "dependencies": [
    "zerobuffer"
  ]
}
```

### Example CMakeLists.txt

```cmake
cmake_minimum_required(VERSION 3.20)
project(your_project)

find_package(zerobuffer CONFIG REQUIRED)

add_executable(your_app main.cpp)
target_link_libraries(your_app PRIVATE zerobuffer::zerobuffer)
```

## Building ZeroBuffer from source with vcpkg dependencies

If you want to build ZeroBuffer from source but use vcpkg for dependencies:

```bash
cd zerobuffer/cpp
mkdir build && cd build
cmake .. -DCMAKE_TOOLCHAIN_FILE=[path to vcpkg]/scripts/buildsystems/vcpkg.cmake
cmake --build .
```

## Features

The vcpkg port supports the following features:

- `tests`: Build the test suite
- `benchmarks`: Build performance benchmarks

To install with features:

```bash
vcpkg install zerobuffer[tests,benchmarks]
```

## Platform Support

- Windows (x64, x86)
- Linux (x64, ARM64)
- macOS (x64, ARM64)

## Troubleshooting

### Boost not found

Make sure you have Boost installed via vcpkg:

```bash
vcpkg install boost-log boost-thread boost-filesystem boost-system
```

### CMake configuration fails

Ensure you're using the vcpkg toolchain file:

```bash
-DCMAKE_TOOLCHAIN_FILE=[path to vcpkg]/scripts/buildsystems/vcpkg.cmake
```