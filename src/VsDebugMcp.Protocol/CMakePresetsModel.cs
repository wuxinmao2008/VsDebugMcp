using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VsDebugMcp.Protocol;

public sealed class CMakeConfigurePreset
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public string Generator { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string BuildType { get; set; } = string.Empty;
    public string BinaryDir { get; set; } = string.Empty;
}

public static class CMakePresetsParser
{
    public static List<CMakeConfigurePreset> ParseConfigurePresets(string jsonContent, string workspaceRoot = "")
    {
        var list = new List<CMakeConfigurePreset>();
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            if (root.TryGetProperty("configurePresets", out var presetsEl) && presetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var presetEl in presetsEl.EnumerateArray())
                {
                    bool hidden = false;
                    if (presetEl.TryGetProperty("hidden", out var hEl))
                    {
                        hidden = hEl.ValueKind == JsonValueKind.True;
                    }

                    if (hidden)
                    {
                        continue;
                    }

                    string name = string.Empty;
                    if (presetEl.TryGetProperty("name", out var nEl))
                    {
                        name = nEl.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string displayName = string.Empty;
                    if (presetEl.TryGetProperty("displayName", out var dEl))
                    {
                        displayName = dEl.GetString() ?? string.Empty;
                    }

                    string description = string.Empty;
                    if (presetEl.TryGetProperty("description", out var descEl))
                    {
                        description = descEl.GetString() ?? string.Empty;
                    }

                    string generator = string.Empty;
                    if (presetEl.TryGetProperty("generator", out var gEl))
                    {
                        generator = gEl.GetString() ?? string.Empty;
                    }

                    string rawBinaryDir = string.Empty;
                    if (presetEl.TryGetProperty("binaryDir", out var bEl))
                    {
                        rawBinaryDir = bEl.GetString() ?? string.Empty;
                    }

                    string arch = string.Empty;
                    if (presetEl.TryGetProperty("architecture", out var aEl))
                    {
                        if (aEl.ValueKind == JsonValueKind.String)
                        {
                            arch = aEl.GetString() ?? string.Empty;
                        }
                        else if (aEl.ValueKind == JsonValueKind.Object && aEl.TryGetProperty("value", out var vEl))
                        {
                            arch = vEl.GetString() ?? string.Empty;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(arch))
                    {
                        if (name.StartsWith("x64", StringComparison.OrdinalIgnoreCase))
                        {
                            arch = "x64";
                        }
                        else if (name.StartsWith("x86", StringComparison.OrdinalIgnoreCase) ||
                                 name.StartsWith("win32", StringComparison.OrdinalIgnoreCase))
                        {
                            arch = "x86";
                        }
                        else if (name.StartsWith("arm64", StringComparison.OrdinalIgnoreCase))
                        {
                            arch = "ARM64";
                        }
                    }

                    string buildType = string.Empty;
                    if (presetEl.TryGetProperty("cacheVariables", out var cvEl) && cvEl.ValueKind == JsonValueKind.Object)
                    {
                        if (cvEl.TryGetProperty("CMAKE_BUILD_TYPE", out var btEl))
                        {
                            if (btEl.ValueKind == JsonValueKind.String)
                            {
                                buildType = btEl.GetString() ?? string.Empty;
                            }
                            else if (btEl.ValueKind == JsonValueKind.Object && btEl.TryGetProperty("value", out var bvEl))
                            {
                                buildType = bvEl.GetString() ?? string.Empty;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(buildType))
                    {
                        if (name.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            buildType = "Debug";
                        }
                        else if (name.IndexOf("RelWithDebInfo", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            buildType = "RelWithDebInfo";
                        }
                        else if (name.IndexOf("MinSizeRel", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            buildType = "MinSizeRel";
                        }
                        else if (name.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            buildType = "Release";
                        }
                    }

                    string evaluatedBinaryDir = string.Empty;
                    if (!string.IsNullOrWhiteSpace(rawBinaryDir))
                    {
                        evaluatedBinaryDir = rawBinaryDir
                            .Replace("${sourceDir}", workspaceRoot.TrimEnd('\\', '/'))
                            .Replace("${presetName}", name);
                    }

                    list.Add(new CMakeConfigurePreset
                    {
                        Name = name,
                        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                        Description = description,
                        Generator = generator,
                        Architecture = arch,
                        BuildType = buildType,
                        BinaryDir = evaluatedBinaryDir
                    });
                }
            }
        }
        catch
        {
            // Return any successfully parsed presets or empty list on invalid JSON
        }

        return list;
    }

    public static List<CMakeConfigurePreset> LoadWorkspacePresets(string workspaceRoot)
    {
        var result = new List<CMakeConfigurePreset>();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return result;
        }

        var presetsPath = Path.Combine(workspaceRoot, "CMakePresets.json");
        if (File.Exists(presetsPath))
        {
            try
            {
                var content = File.ReadAllText(presetsPath);
                result.AddRange(ParseConfigurePresets(content, workspaceRoot));
            }
            catch
            {
            }
        }

        var userPresetsPath = Path.Combine(workspaceRoot, "CMakeUserPresets.json");
        if (File.Exists(userPresetsPath))
        {
            try
            {
                var content = File.ReadAllText(userPresetsPath);
                var userPresets = ParseConfigurePresets(content, workspaceRoot);
                foreach (var up in userPresets)
                {
                    var existing = result.FindIndex(p => string.Equals(p.Name, up.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing >= 0)
                    {
                        result[existing] = up;
                    }
                    else
                    {
                        result.Add(up);
                    }
                }
            }
            catch
            {
            }
        }

        return result;
    }
}
