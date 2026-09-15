add_library(sample_calculator STATIC
    src/math_ops.cpp
)

target_include_directories(sample_calculator PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(sample_calculator PUBLIC
    sample_core
)

target_compile_features(sample_calculator PUBLIC cxx_std_17)
