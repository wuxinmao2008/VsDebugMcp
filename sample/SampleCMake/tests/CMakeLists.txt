add_executable(sample_tests
    test_math.cpp
)

target_link_libraries(sample_tests PRIVATE
    sample_calculator
    sample_core
)

target_compile_features(sample_tests PRIVATE cxx_std_17)

if(MSVC)
    target_compile_options(sample_tests PRIVATE /utf-8)
endif()

# 1. 成功测试用例：用于验证正常执行、控制台输出透传与中文日志编码
add_test(NAME MathOperationsTest COMMAND sample_tests)

# 2. 模拟失败测试用例：用于验证失败捕获、FAIL! 错误日志透传与非零退出码
add_test(NAME MathOperationsFailureTest COMMAND sample_tests --fail)
