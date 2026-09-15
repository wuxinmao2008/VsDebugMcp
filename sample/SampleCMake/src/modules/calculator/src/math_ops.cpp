#include "calculator/math_ops.h"
#include "core/logger.h"
#include <stdexcept>
#include <string>

namespace SampleCMake::Modules {

int MathOps::Add(int a, int b) {
    Core::Logger::Info("Computing Add(" + std::to_string(a) + ", " + std::to_string(b) + ")");
    return a + b;
}

int MathOps::Subtract(int a, int b) {
    Core::Logger::Info("Computing Subtract(" + std::to_string(a) + ", " + std::to_string(b) + ")");
    return a - b;
}

int MathOps::Multiply(int a, int b) {
    Core::Logger::Info("Computing Multiply(" + std::to_string(a) + ", " + std::to_string(b) + ")");
    return a * b;
}

double MathOps::Divide(int a, int b) {
    Core::Logger::Info("Computing Divide(" + std::to_string(a) + ", " + std::to_string(b) + ")");
    if (b == 0) {
        Core::Logger::Error("Division by zero error!");
        throw std::invalid_argument("Division by zero");
    }
    return static_cast<double>(a) / b;
}

int MathOps::Factorial(int n) {
    if (n < 0) {
        throw std::invalid_argument("Negative number has no factorial");
    }
    int result = 1;
    for (int i = 2; i <= n; ++i) {
        result *= i;
    }
    return result;
}

} // namespace SampleCMake::Modules
