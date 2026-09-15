#pragma once

namespace SampleCMake::Modules {

class MathOps {
public:
    static int Add(int a, int b);
    static int Subtract(int a, int b);
    static int Multiply(int a, int b);
    static double Divide(int a, int b);
    static int Factorial(int n);
};

} // namespace SampleCMake::Modules
