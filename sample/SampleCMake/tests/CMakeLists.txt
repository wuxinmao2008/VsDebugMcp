add_executable(sample_tests
    test_math.cpp
)

target_link_libraries(sample_tests PRIVATE
    sample_calculator
    sample_core
)

target_compile_features(sample_tests PRIVATE cxx_std_17)

add_test(NAME MathOperationsTest COMMAND sample_tests)
