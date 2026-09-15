#include "calculator/math_ops.h"
#include "core/logger.h"
#include <cassert>
#include <iostream>

int main() {
    SampleCMake::Core::Logger::Info("Running MathOperationsTest...");

    assert(SampleCMake::Modules::MathOps::Add(2, 3) == 5);
    assert(SampleCMake::Modules::MathOps::Subtract(10, 4) == 6);
    assert(SampleCMake::Modules::MathOps::Multiply(3, 7) == 21);
    assert(SampleCMake::Modules::MathOps::Divide(10, 2) == 5.0);
    assert(SampleCMake::Modules::MathOps::Factorial(0) == 1);
    assert(SampleCMake::Modules::MathOps::Factorial(4) == 24);

    bool threw = false;
    try {
        SampleCMake::Modules::MathOps::Divide(1, 0);
    } catch (const std::invalid_argument&) {
        threw = true;
    }
    assert(threw);

    SampleCMake::Core::Logger::Info("All tests passed!");
    std::cout << "ALL_TESTS_PASSED" << std::endl;
    return 0;
}
