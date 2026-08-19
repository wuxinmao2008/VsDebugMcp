using System;
using System.Linq;

namespace VsDebugMcp.Protocol;

public static class PipeNames
{
    public static string ForCurrentUser()
    {
        return $"VsDebugMcp.Bridge.v1.{GetCurrentUserSuffix()}";
    }


    public static string ForMcpHost() => "VsDebugMcp.Host.v1";

    private static string GetCurrentUserSuffix()
    {
        var user = new string(Environment.UserName
            .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
            .ToArray());
        return string.IsNullOrEmpty(user) ? "user" : user;
    }
}