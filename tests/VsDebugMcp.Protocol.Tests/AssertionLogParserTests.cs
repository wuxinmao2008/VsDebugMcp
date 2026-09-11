using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class AssertionLogParserTests
{
    [Fact]
    public void ScanLogsForAssertionOrException_CrtAssert_ParsesExpressionAndLocation()
    {
        var log = @"Some standard output log
Assertion failed: buffer != nullptr, file D:\Code\Project\Buffer.cpp, line 108
Another log line";

        var response = new DebuggerGetExceptionInfoResponse();
        AssertionLogParser.ScanLogsForAssertionOrException(log, response);

        Assert.True(response.HasException);
        Assert.True(response.AssertionFailed);
        Assert.Equal("buffer != nullptr", response.AssertionExpression);
        Assert.Equal(@"D:\Code\Project\Buffer.cpp", response.AssertionFile);
        Assert.Equal(108, response.AssertionLine);
        Assert.Equal("AssertionFailure", response.ExceptionType);
        Assert.Equal("Assertion failed: buffer != nullptr", response.Message);
        Assert.Equal(@"D:\Code\Project\Buffer.cpp", response.Source);
    }

    [Fact]
    public void ScanLogsForAssertionOrException_NetAssert_ParsesMessage()
    {
        var log = @"Test running...
---- DEBUG ASSERTION FAILED ----
Index out of bounds in collection lookup
   at MyApp.Service.Get(Int32 index)";

        var response = new DebuggerGetExceptionInfoResponse();
        AssertionLogParser.ScanLogsForAssertionOrException(log, response);

        Assert.True(response.HasException);
        Assert.True(response.AssertionFailed);
        Assert.Equal("DebugAssertionFailure", response.ExceptionType);
        Assert.Equal("Debug assertion failed: Index out of bounds in collection lookup", response.Message);
    }

    [Fact]
    public void ScanLogsForAssertionOrException_NativeException_ParsesErrorCodeAndDescription()
    {
        var log = @"Loaded 'ucrtbase.dll'.
First-chance exception at 0x00007FFE8B2C4F10 (ntdll.dll) in NativeApp.exe: 0xC0000005: Access violation reading location 0x0000000000000000.
The thread 0x4d28 has exited with code 0 (0x0).";

        var response = new DebuggerGetExceptionInfoResponse();
        AssertionLogParser.ScanLogsForAssertionOrException(log, response);

        Assert.True(response.HasException);
        Assert.Equal("0xC0000005", response.HResult);
        Assert.Equal("0xC0000005", response.ExceptionType);
        Assert.Equal("Access violation reading location 0x0000000000000000.", response.Message);
    }

    [Fact]
    public void ScanLogsForAssertionOrException_EmptyOrNormalLog_DoesNothing()
    {
        var log = @"Build started...
Project compiled successfully.
Process exited with code 0.";

        var response = new DebuggerGetExceptionInfoResponse();
        AssertionLogParser.ScanLogsForAssertionOrException(log, response);

        Assert.False(response.HasException);
        Assert.Null(response.AssertionFailed);
        Assert.Null(response.Message);
    }
}
