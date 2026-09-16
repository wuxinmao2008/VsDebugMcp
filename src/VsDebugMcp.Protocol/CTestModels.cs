using System;
using System.Collections.Generic;
using System.IO;
#if NETSTANDARD2_0
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
#endif
using System.Xml.Linq;

namespace VsDebugMcp.Protocol;

public sealed class CTestItem
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
}

public sealed class CTestRunResult
{
    public int TotalTests { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, CTestCaseOutcome> TestOutcomes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CTestCaseOutcome
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = "Passed"; // "Passed", "Failed", "Skipped"
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SystemOut { get; set; }
}

public static class CTestParser
{
    public static List<CTestItem> ParseDiscoveryJson(string jsonContent)
    {
        var result = new List<CTestItem>();
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return result;
        }

#if NETSTANDARD2_0
        try
        {
            int firstBrace = jsonContent.IndexOf('{');
            if (firstBrace > 0)
            {
                jsonContent = jsonContent.Substring(firstBrace);
            }
            int lastBrace = jsonContent.LastIndexOf('}');
            if (lastBrace >= 0 && lastBrace < jsonContent.Length - 1)
            {
                jsonContent = jsonContent.Substring(0, lastBrace + 1);
            }

            var root = JObject.Parse(jsonContent);

            // Parse file paths from backtraceGraph
            var filePaths = new List<string>();
            var nodes = new List<(int fileIndex, int line)>();
            if (root["backtraceGraph"] is JObject bgEl)
            {
                if (bgEl["files"] is JArray filesEl)
                {
                    foreach (var f in filesEl)
                    {
                        filePaths.Add((string?)f ?? string.Empty);
                    }
                }

                if (bgEl["nodes"] is JArray nodesEl)
                {
                    foreach (var n in nodesEl)
                    {
                        int fileIdx = (int?)n["file"] ?? -1;
                        int line = (int?)n["line"] ?? 0;
                        nodes.Add((fileIdx, line));
                    }
                }
            }

            if (root["tests"] is JArray testsEl)
            {
                foreach (var tEl in testsEl)
                {
                    string name = (string?)tEl["name"] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string command = string.Empty;
                    if (tEl["command"] is JArray cmdEl)
                    {
                        foreach (var c in cmdEl)
                        {
                            command = (string?)c ?? string.Empty;
                            break;
                        }
                    }

                    string workingDir = string.Empty;
                    if (tEl["properties"] is JArray propsEl)
                    {
                        foreach (var p in propsEl)
                        {
                            if (string.Equals((string?)p["name"], "WORKING_DIRECTORY", StringComparison.OrdinalIgnoreCase))
                            {
                                workingDir = (string?)p["value"] ?? string.Empty;
                                break;
                            }
                        }
                    }

                    string filePath = string.Empty;
                    int lineNumber = 0;
                    var btVal = (int?)tEl["backtrace"];
                    if (btVal.HasValue)
                    {
                        int btIdx = btVal.Value;
                        if (btIdx >= 0 && btIdx < nodes.Count)
                        {
                            var node = nodes[btIdx];
                            if (node.fileIndex >= 0 && node.fileIndex < filePaths.Count)
                            {
                                filePath = filePaths[node.fileIndex];
                                lineNumber = node.line;
                            }
                        }
                    }

                    result.Add(new CTestItem
                    {
                        Name = name,
                        Command = command,
                        WorkingDirectory = workingDir,
                        FilePath = filePath,
                        LineNumber = lineNumber
                    });
                }
            }
        }
        catch
        {
        }
#else
        try
        {
            int firstBrace = jsonContent.IndexOf('{');
            if (firstBrace > 0)
            {
                jsonContent = jsonContent.Substring(firstBrace);
            }
            int lastBrace = jsonContent.LastIndexOf('}');
            if (lastBrace >= 0 && lastBrace < jsonContent.Length - 1)
            {
                jsonContent = jsonContent.Substring(0, lastBrace + 1);
            }

            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Parse file paths from backtraceGraph
            var filePaths = new List<string>();
            var nodes = new List<(int fileIndex, int line)>();
            if (root.TryGetProperty("backtraceGraph", out var bgEl))
            {
                if (bgEl.TryGetProperty("files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in filesEl.EnumerateArray())
                    {
                        filePaths.Add(f.GetString() ?? string.Empty);
                    }
                }

                if (bgEl.TryGetProperty("nodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in nodesEl.EnumerateArray())
                    {
                        int fileIdx = n.TryGetProperty("file", out var fEl) ? fEl.GetInt32() : -1;
                        int line = n.TryGetProperty("line", out var lEl) ? lEl.GetInt32() : 0;
                        nodes.Add((fileIdx, line));
                    }
                }
            }

            if (root.TryGetProperty("tests", out var testsEl) && testsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tEl in testsEl.EnumerateArray())
                {
                    string name = tEl.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string command = string.Empty;
                    if (tEl.TryGetProperty("command", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in cmdEl.EnumerateArray())
                        {
                            command = c.GetString() ?? string.Empty;
                            break;
                        }
                    }

                    string workingDir = string.Empty;
                    if (tEl.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in propsEl.EnumerateArray())
                        {
                            if (p.TryGetProperty("name", out var pnEl) &&
                                string.Equals(pnEl.GetString(), "WORKING_DIRECTORY", StringComparison.OrdinalIgnoreCase))
                            {
                                workingDir = p.TryGetProperty("value", out var pvEl) ? pvEl.GetString() ?? string.Empty : string.Empty;
                                break;
                            }
                        }
                    }

                    string filePath = string.Empty;
                    int lineNumber = 0;
                    if (tEl.TryGetProperty("backtrace", out var btEl) && btEl.ValueKind == JsonValueKind.Number)
                    {
                        int btIdx = btEl.GetInt32();
                        if (btIdx >= 0 && btIdx < nodes.Count)
                        {
                            var node = nodes[btIdx];
                            if (node.fileIndex >= 0 && node.fileIndex < filePaths.Count)
                            {
                                filePath = filePaths[node.fileIndex];
                                lineNumber = node.line;
                            }
                        }
                    }

                    result.Add(new CTestItem
                    {
                        Name = name,
                        Command = command,
                        WorkingDirectory = workingDir,
                        FilePath = filePath,
                        LineNumber = lineNumber
                    });
                }
            }
        }
        catch
        {
        }
#endif

        return result;
    }

    public static CTestRunResult ParseJUnitXml(string xmlContent)
    {
        var result = new CTestRunResult();
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return result;
        }

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var suite = doc.Root?.Element("testsuite") ?? doc.Root;
            if (suite != null)
            {
                if (int.TryParse(suite.Attribute("tests")?.Value, out var total))
                    result.TotalTests = total;
                if (int.TryParse(suite.Attribute("failures")?.Value, out var failures))
                    result.FailedCount = failures;
                if (int.TryParse(suite.Attribute("skipped")?.Value, out var skipped))
                    result.SkippedCount = skipped;
                if (double.TryParse(suite.Attribute("time")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var totalSeconds))
                    result.Duration = TimeSpan.FromSeconds(totalSeconds);

                foreach (var tc in suite.Elements("testcase"))
                {
                    string name = tc.Attribute("name")?.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    double durationMs = 0;
                    if (double.TryParse(tc.Attribute("time")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tcSec))
                    {
                        durationMs = tcSec * 1000.0;
                    }

                    var failureEl = tc.Element("failure");
                    var sysOutEl = tc.Element("system-out");

                    string state = "Passed";
                    string? errMsg = null;
                    if (failureEl != null)
                    {
                        state = "Failed";
                        errMsg = failureEl.Attribute("message")?.Value ?? failureEl.Value.Trim();
                    }

                    result.TestOutcomes[name] = new CTestCaseOutcome
                    {
                        Name = name,
                        State = state,
                        DurationMs = durationMs,
                        ErrorMessage = errMsg,
                        SystemOut = sysOutEl?.Value.Trim()
                    };
                }

                result.PassedCount = Math.Max(0, result.TotalTests - result.FailedCount - result.SkippedCount);
            }
        }
        catch
        {
        }

        return result;
    }
}
