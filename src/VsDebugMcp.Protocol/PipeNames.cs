using System;
using System.Linq;

namespace VsDebugMcp.Protocol;

public static class PipeNames
{
    public static string ForHostControl() => $"VsDebugMcp.Host.Control.v2.{GetCurrentUserSuffix()}";

    public static string ForVisualStudioInstance(string vsInstanceId)
    {
        if (string.IsNullOrWhiteSpace(vsInstanceId))
        {
            throw new ArgumentException("A Visual Studio instance ID is required.", nameof(vsInstanceId));
        }

        var suffix = Sanitize(vsInstanceId);
        if (string.IsNullOrEmpty(suffix))
        {
            throw new ArgumentException("The Visual Studio instance ID contains no valid pipe name characters.", nameof(vsInstanceId));
        }

        return $"VsDebugMcp.Bridge.v2.{GetCurrentUserSuffix()}.{suffix}";
    }

    private static string GetCurrentUserSuffix()
    {
        var user = Sanitize(Environment.UserName);
        return string.IsNullOrEmpty(user) ? "user" : user;
    }

    private static string Sanitize(string value) => new(value
        .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
        .Take(96)
        .ToArray());
}