#include "core/logger.h"
#include "calculator/math_ops.h"
#include <iostream>

int main() {
    SampleCMake::Core::Logger::Info("SampleCMakeApp starting...");

    int a = 20;
    int b = 6;

    int sum = SampleCMake::Modules::MathOps::Add(a, b);
    int diff = SampleCMake::Modules::MathOps::Subtract(a, b);
    int prod = SampleCMake::Modules::MathOps::Multiply(a, b);
    double quot = SampleCMake::Modules::MathOps::Divide(a, b);
    int fact = SampleCMake::Modules::MathOps::Factorial(5);

    std::cout << "Results:" << std::endl;
    std::cout << "  " << a << " + " << b << " = " << sum << std::endl;
    std::cout << "  " << a << " - " << b << " = " << diff << std::endl;
    std::cout << "  " << a << " * " << b << " = " << prod << std::endl;
    std::cout << "  " << a << " / " << b << " = " << quot << std::endl;
    std::cout << "  5! = " << fact << std::endl;

    SampleCMake::Core::Logger::Info("SampleCMakeApp finished successfully.");
    return 0;
}
