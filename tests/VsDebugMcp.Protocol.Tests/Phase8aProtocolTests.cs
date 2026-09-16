using System.Collections.Generic;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class Phase8aProtocolTests
{
    [Fact]
    public void DebuggerGetCallStackRequest_RoundTrips_WithNewFilters()
    {
        var request = new DebuggerGetCallStackRequest
        {
            ThreadId = 1234,
            MaxFrames = 25,
            UserCodeOnly = true,
            CollapseExternal = true
        };

        var json = BridgeJson.Serialize(request);
        var deserialized = BridgeJson.Deserialize<DebuggerGetCallStackRequest>(json);

        Assert.Equal(1234, deserialized.ThreadId);
        Assert.Equal(25, deserialized.MaxFrames);
        Assert.True(deserialized.UserCodeOnly);
        Assert.True(deserialized.CollapseExternal);
    }

    [Fact]
    public void DebuggerGetCallStackResponse_RoundTrips_WithUserFrameMetrics()
    {
        var response = new DebuggerGetCallStackResponse
        {
            VsInstanceId = "vs-test-1",
            ThreadId = 42,
            ThreadName = "MainThread",
            TotalFrames = 15,
            Truncated = false,
            FirstUserFrameIndex = 3,
            UserCodeFramesCount = 2,
            Frames = new List<StackFrameInfo>
            {
                new()
                {
                    FrameIndex = 0,
                    FunctionName = "[External Code: Qt & Runtime - 3 frames]",
                    UserCode = false
                },
                new()
                {
                    FrameIndex = 3,
                    FunctionName = "SystemParaWidget::~SystemParaWidget()",
                    FileName = "D:\\Projects\\QtApp\\SystemParaWidget.cpp",
                    LineNumber = 56,
                    UserCode = true,
                    Module = "QtApp.exe"
                }
            }
        };

        var json = BridgeJson.Serialize(response);
        var deserialized = BridgeJson.Deserialize<DebuggerGetCallStackResponse>(json);

        Assert.Equal("vs-test-1", deserialized.VsInstanceId);
        Assert.Equal(42, deserialized.ThreadId);
        Assert.Equal(3, deserialized.FirstUserFrameIndex);
        Assert.Equal(2, deserialized.UserCodeFramesCount);
        Assert.Equal(2, deserialized.Frames.Count);
        Assert.False(deserialized.Frames[0].UserCode);
        Assert.True(deserialized.Frames[1].UserCode);
        Assert.Equal("SystemParaWidget.cpp", System.IO.Path.GetFileName(deserialized.Frames[1].FileName));
    }

    [Fact]
    public void DebuggerClearBreakpointsRequest_RoundTrips_WithSessionOnly()
    {
        var request = new DebuggerClearBreakpointsRequest
        {
            ClearAll = false,
            SessionOnly = true,
            VsInstanceId = "vs-test-1"
        };

        var json = BridgeJson.Serialize(request);
        var deserialized = BridgeJson.Deserialize<DebuggerClearBreakpointsRequest>(json);

        Assert.False(deserialized.ClearAll);
        Assert.True(deserialized.SessionOnly);
        Assert.Equal("vs-test-1", deserialized.VsInstanceId);
    }
}
