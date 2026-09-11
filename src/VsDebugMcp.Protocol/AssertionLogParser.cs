using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VsDebugMcp.Protocol;

public static class AssertionLogParser
{
    private static readonly Regex s_crtAssertRegex = new(
        @"Assertion failed:\s*(?<expr>.+?),\s*file\s*(?<file>.+?),\s*line\s*(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_nativeExceptionRegex = new(
        @"(?:First-chance exception|Exception thrown|Unhandled exception) at 0x[0-9A-Fa-f]+.*?: (?<code>0x[0-9A-Fa-f]+): (?<desc>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_netAssertRegex = new(
        @"---- DEBUG ASSERTION FAILED ----\r?\n(?<msg>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void ScanLogsForAssertionOrException(string logs, DebuggerGetExceptionInfoResponse response)
    {
        if (string.IsNullOrWhiteSpace(logs) || response == null) return;

        // 1. Scan for CRT Assertion Failure: "Assertion failed: <expr>, file <file>, line <line>"
        var crtMatches = s_crtAssertRegex.Matches(logs);
        if (crtMatches.Count > 0)
        {
            var lastMatch = crtMatches[crtMatches.Count - 1];
            response.HasException = true;
            response.AssertionFailed = true;
            response.AssertionExpression = lastMatch.Groups["expr"].Value.Trim();
            response.AssertionFile = lastMatch.Groups["file"].Value.Trim();
            if (int.TryParse(lastMatch.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineVal))
            {
                response.AssertionLine = lineVal;
            }
            response.ExceptionType ??= "AssertionFailure";
            response.Message = $"Assertion failed: {response.AssertionExpression}";
            response.Source ??= response.AssertionFile;
            return;
        }

        // 2. Scan for .NET Debug.Assert
        var netMatches = s_netAssertRegex.Matches(logs);
        if (netMatches.Count > 0)
        {
            var lastMatch = netMatches[netMatches.Count - 1];
            response.HasException = true;
            response.AssertionFailed = true;
            response.ExceptionType ??= "DebugAssertionFailure";
            var msg = lastMatch.Groups["msg"].Value.Trim();
            response.Message = string.IsNullOrWhiteSpace(msg) ? "Debug assertion failed" : $"Debug assertion failed: {msg}";
            return;
        }

        // 3. Scan for Native Exception code & message
        var nativeMatches = s_nativeExceptionRegex.Matches(logs);
        if (nativeMatches.Count > 0)
        {
            var lastMatch = nativeMatches[nativeMatches.Count - 1];
            response.HasException = true;
            response.HResult ??= lastMatch.Groups["code"].Value.Trim();
            response.ExceptionType ??= lastMatch.Groups["code"].Value.Trim();
            if (string.IsNullOrEmpty(response.Message) || response.Message.StartsWith("Debugger paused due to an exception"))
            {
                response.Message = lastMatch.Groups["desc"].Value.Trim();
            }
        }
    }
}
