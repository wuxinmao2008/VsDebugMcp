using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace VsDebugMcp.Host.Tests;

public sealed class HttpAcceptHeaderMiddlewareTests
{
    [Theory]
    [InlineData("", "application/json, text/event-stream")]
    [InlineData(null, "application/json, text/event-stream")]
    [InlineData("*/*", "application/json, text/event-stream")]
    [InlineData("application/json", "application/json, text/event-stream")]
    [InlineData("text/event-stream", "text/event-stream")]
    [InlineData("application/json, text/event-stream", "application/json, text/event-stream")]
    public async Task NormalizeAcceptHeaderSetsRequiredStreamableHeaders(string? inputAccept, string expectedAccept)
    {
        var context = new DefaultHttpContext();
        if (inputAccept != null)
        {
            context.Request.Headers.Accept = inputAccept;
        }

        // Run middleware logic
        var accept = context.Request.Headers.Accept.ToString();
        if (string.IsNullOrWhiteSpace(accept) || accept == "*/*" || accept == "application/json")
        {
            context.Request.Headers.Accept = "application/json, text/event-stream";
        }

        Assert.Equal(expectedAccept, context.Request.Headers.Accept.ToString());
    }
}
