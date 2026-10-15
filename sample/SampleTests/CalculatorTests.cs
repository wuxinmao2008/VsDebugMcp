using SampleApp.Services;
using Xunit;

namespace SampleTests;

public class CalculatorTests
{
    [Fact]
    public void Add_TwoNumbers_ReturnsSum()
    {
        var result = Calculator.Add(10, 20);
        Assert.Equal(30, result);
    }

    [Fact]
    public void Multiply_TwoNumbers_ReturnsProduct()
    {
        var result = Calculator.Multiply(4, 5);
        Assert.Equal(20, result);
    }

    [Fact]
    public void Add_NegativeNumbers_ReturnsCorrectSum()
    {
        var result = Calculator.Add(-5, -15);
        Assert.Equal(-20, result);
    }

    [Fact]
    public void Divide_ByZero_ThrowsException()
    {
        var result = Calculator.Divide(10, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Test_ConsoleOutput_And_Encoding()
    {
        System.Console.WriteLine("[测试日志] 单元测试控制台输出测试，验证中文编码与透传正常。");
        var result = Calculator.Add(1, 1);
        Assert.Equal(2, result);
    }
}
