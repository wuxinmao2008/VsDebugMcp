#include "calculator/math_ops.h"
#include "core/logger.h"
#include <cassert>
#include <cstring>
#include <iostream>

int main(int argc, char* argv[]) {
    bool shouldFail = false;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--fail") == 0) {
            shouldFail = true;
            break;
        }
    }

    if (shouldFail) {
        std::cout << "[测试日志] 启动模拟失败用例 MathOperationsFailureTest..." << std::endl;
        std::cout << "[用例详情] 正在测试 MathOps::Add(2, 3) 是否等于预期错误值 999..." << std::endl;
        std::cerr << "FAIL! : MathOps::Add(2, 3) == 999 模拟断言失败 (实际值: 5, 期望值: 999)" << std::endl;
        std::cout << "[测试总结] Totals: 0 passed, 1 failed, 0 skipped" << std::endl;
        return 1;
    }

    SampleCMake::Core::Logger::Info("Running MathOperationsTest...");
    std::cout << "[测试日志] 启动数学运算测试 MathOperationsTest..." << std::endl;

    assert(SampleCMake::Modules::MathOps::Add(2, 3) == 5);
    std::cout << "[用例通过] 验证加法运算: 2 + 3 = 5" << std::endl;

    assert(SampleCMake::Modules::MathOps::Subtract(10, 4) == 6);
    std::cout << "[用例通过] 验证减法运算: 10 - 4 = 6" << std::endl;

    assert(SampleCMake::Modules::MathOps::Multiply(3, 7) == 21);
    std::cout << "[用例通过] 验证乘法运算: 3 * 7 = 21" << std::endl;

    assert(SampleCMake::Modules::MathOps::Divide(10, 2) == 5.0);
    std::cout << "[用例通过] 验证除法运算: 10 / 2 = 5.0" << std::endl;

    assert(SampleCMake::Modules::MathOps::Factorial(0) == 1);
    assert(SampleCMake::Modules::MathOps::Factorial(4) == 24);
    std::cout << "[用例通过] 验证阶乘运算: 0! = 1, 4! = 24" << std::endl;

    bool threw = false;
    try {
        SampleCMake::Modules::MathOps::Divide(1, 0);
    } catch (const std::invalid_argument&) {
        threw = true;
    }
    assert(threw);
    std::cout << "[用例通过] 验证除以零异常捕获成功" << std::endl;

    SampleCMake::Core::Logger::Info("All tests passed!");
    std::cout << "[测试完成] 全部 6 项子用例测试通过！Totals: 6 passed, 0 failed, 0 skipped" << std::endl;
    std::cout << "ALL_TESTS_PASSED" << std::endl;
    return 0;
}
