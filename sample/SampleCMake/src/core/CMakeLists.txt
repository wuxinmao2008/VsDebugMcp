add_library(sample_core STATIC
    src/logger.cpp
)

target_include_directories(sample_core PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_compile_features(sample_core PUBLIC cxx_std_17)
