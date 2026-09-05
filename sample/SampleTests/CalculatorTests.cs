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
}
