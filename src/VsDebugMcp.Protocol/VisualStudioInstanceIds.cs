using System;
using System.Globalization;

namespace VsDebugMcp.Protocol;

public static class VisualStudioInstanceIds
{
    public static string Create(int processId, long processStartTimeUtcTicks) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "vs-{0}-{1:x16}",
            processId,
            processStartTimeUtcTicks);
}