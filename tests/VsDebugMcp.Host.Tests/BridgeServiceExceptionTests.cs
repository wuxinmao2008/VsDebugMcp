using VsDebugMcp.Host;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Host.Tests;

public sealed class BridgeServiceExceptionTests
{
    [Theory]
    [InlineData(BridgeErrorCodes.TestRunBusy, false)]
    [InlineData(BridgeErrorCodes.TestRunNotFound, false)]
    [InlineData(BridgeErrorCodes.TestNotFound, false)]
    [InlineData(BridgeErrorCodes.TestWindowUnavailable, true)]
    [InlineData(BridgeErrorCodes.ProcessNotFound, false)]
    [InlineData(BridgeErrorCodes.FileNotFound, false)]
    [InlineData(BridgeErrorCodes.InvalidNavigationTarget, false)]
    [InlineData(BridgeErrorCodes.ActiveDocumentUnavailable, false)]
    [InlineData(BridgeErrorCodes.DebuggerRunningCannotBuild, false)]
    [InlineData(BridgeErrorCodes.ThreadNotFound, false)]
    [InlineData(BridgeErrorCodes.InvalidNextStatement, false)]
    [InlineData(BridgeErrorCodes.BreakpointNotFound, false)]
    [InlineData(BridgeErrorCodes.InvalidBreakpointTarget, false)]
    [InlineData(BridgeErrorCodes.ConfigurationNotFound, false)]
    [InlineData(BridgeErrorCodes.OutputPaneNotFound, false)]
    [InlineData(BridgeErrorCodes.CannotSwitchConfigurationWhileDebugging, false)]
    [InlineData(BridgeErrorCodes.EngineNotFound, false)]
    [InlineData(BridgeErrorCodes.NoSolutionProcessesFound, false)]
    public void FromBridgeMapsTestExplorerErrorCodes(string code, bool expectedRetryable)
    {
        var rpcException = new BridgeRpcException(code, $"Error for {code}", expectedRetryable);
        var serviceException = BridgeServiceException.FromBridge(rpcException);

        Assert.Equal(code, serviceException.Code);
        Assert.Equal(expectedRetryable, serviceException.Retryable);
        Assert.Equal($"Error for {code}", serviceException.Message);
    }
}
