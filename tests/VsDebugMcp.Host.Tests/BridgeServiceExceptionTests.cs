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
    public void FromBridgeMapsTestExplorerErrorCodes(string code, bool expectedRetryable)
    {
        var rpcException = new BridgeRpcException(code, $"Error for {code}", expectedRetryable);
        var serviceException = BridgeServiceException.FromBridge(rpcException);

        Assert.Equal(code, serviceException.Code);
        Assert.Equal(expectedRetryable, serviceException.Retryable);
        Assert.Equal($"Error for {code}", serviceException.Message);
    }
}
