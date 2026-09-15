using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE80;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal static class CMakeWorkspaceState
{
    private static readonly ConcurrentDictionary<string, string> _activePresets = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCMakeWorkspaceRoot(DTE2? dte, out string workspaceRoot)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        workspaceRoot = string.Empty;

        if (dte?.Solution == null || !dte.Solution.IsOpen)
        {
            return false;
        }

        string slnPath = dte.Solution.FullName;
        if (!string.IsNullOrWhiteSpace(slnPath))
        {
            if (Directory.Exists(slnPath))
            {
                if (File.Exists(Path.Combine(slnPath, "CMakeLists.txt")) ||
                    File.Exists(Path.Combine(slnPath, "CMakePresets.json")))
                {
                    workspaceRoot = Path.GetFullPath(slnPath);
                    return true;
                }
            }
            else if (File.Exists(slnPath))
            {
                string? dir = Path.GetDirectoryName(slnPath);
                if (!string.IsNullOrEmpty(dir) &&
                    (File.Exists(Path.Combine(dir, "CMakeLists.txt")) ||
                     File.Exists(Path.Combine(dir, "CMakePresets.json"))))
                {
                    workspaceRoot = Path.GetFullPath(dir);
                    return true;
                }
            }
        }

        return false;
    }

    public static string GetActivePreset(string workspaceRoot, List<CMakeConfigurePreset> availablePresets)
    {
        if (availablePresets == null || availablePresets.Count == 0)
        {
            return string.Empty;
        }

        if (_activePresets.TryGetValue(workspaceRoot, out var current) &&
            availablePresets.Any(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase)))
        {
            return current;
        }

        // Default to x64-Debug if present, otherwise first available
        var defaultPreset = availablePresets.FirstOrDefault(p => string.Equals(p.Name, "x64-Debug", StringComparison.OrdinalIgnoreCase))
                         ?? availablePresets.First();

        _activePresets[workspaceRoot] = defaultPreset.Name;
        return defaultPreset.Name;
    }

    public static void SetActivePreset(string workspaceRoot, string presetName)
    {
        _activePresets[workspaceRoot] = presetName;
    }
}
