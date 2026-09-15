cmake_minimum_required(VERSION 3.20)

project(SampleCMake
    VERSION 1.0.0
    DESCRIPTION "Sample nested CMake project for Visual Studio MCP integration testing"
    LANGUAGES CXX
)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Enable testing option
option(BUILD_TESTING "Build unit tests" ON)

# Add nested source tree
add_subdirectory(src)

# Add tests if enabled
if(BUILD_TESTING)
    enable_testing()
    add_subdirectory(tests)
endif()
