using System;
using System.Linq;

namespace VsDebugMcp.Protocol;

public static class PipeNames
{
    public static string ForCurrentUser()
    {
        var user = new string(Environment.UserName
            .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
            .ToArray());
        return $"VsDebugMcp.Bridge.v1.{user}";
    }
}