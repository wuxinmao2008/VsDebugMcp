using System.Diagnostics;
using System.Text.Json;

namespace VsDebugMcp.Host.Tests;

public class McpStdioTests
{
    [Fact]
    public async Task ListsAvailableToolsOverStdio()
    {
        var hostDll = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "VsDebugMcp.Host",
            "bin",
            "Debug",
            "net8.0",
            "VsDebugMcp.Host.dll");
        Assert.True(File.Exists(hostDll), $"Host output not found: {hostDll}");

        using var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{hostDll}\" --mcp",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not start the MCP host.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await WriteMessageAsync(
            process,
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"VsDebugMcp.Tests","version":"1.0"}}}
            """);
        using var initialize = await ReadResponseAsync(process, 1, cancellation.Token);
        Assert.Equal("2.0", initialize.RootElement.GetProperty("jsonrpc").GetString());

        await WriteMessageAsync(process, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        await WriteMessageAsync(process, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
        using var toolsResponse = await ReadResponseAsync(process, 2, cancellation.Token);
        var names = toolsResponse.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            [
                "vs_cancel_build",
                "vs_capabilities",
                "vs_get_build_status",
                "vs_get_errors",
                "vs_get_output_window_logs",
                "vs_get_projects_in_solution",
                "vs_health",
                "vs_run_build"
            ],
            names);

        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray();
        var runBuild = tools.Single(tool => tool.GetProperty("name").GetString() == "vs_run_build");
        var runProperties = runBuild.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(runProperties.TryGetProperty("configuration", out _));
        Assert.True(runProperties.TryGetProperty("platform", out _));
        var getStatus = tools.Single(tool => tool.GetProperty("name").GetString() == "vs_get_build_status");
        Assert.Contains(
            "buildTaskId",
            getStatus.GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        var getErrors = tools.Single(tool => tool.GetProperty("name").GetString() == "vs_get_errors");
        var errorProperties = getErrors.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(errorProperties.TryGetProperty("buildTaskId", out _));
        Assert.True(errorProperties.TryGetProperty("severities", out _));
        Assert.True(errorProperties.TryGetProperty("project", out _));
        Assert.True(errorProperties.TryGetProperty("file", out _));
        Assert.True(errorProperties.TryGetProperty("maxCount", out _));
        if (getErrors.GetProperty("inputSchema").TryGetProperty("required", out var requiredErrors))
        {
            Assert.Empty(requiredErrors.EnumerateArray());
        }
        var annotations = getErrors.GetProperty("annotations");
        Assert.True(annotations.GetProperty("readOnlyHint").GetBoolean());
        Assert.True(annotations.GetProperty("idempotentHint").GetBoolean());
        var getOutput = tools.Single(tool => tool.GetProperty("name").GetString() == "vs_get_output_window_logs");
        var outputProperties = getOutput.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(outputProperties.TryGetProperty("source", out _));
        Assert.True(outputProperties.TryGetProperty("maxChars", out _));

        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellation.Token);
        Assert.Equal(0, process.ExitCode);
        await stderrTask;
    }

    private static async Task WriteMessageAsync(Process process, string json)
    {
        await process.StandardInput.WriteLineAsync(json.ReplaceLineEndings(string.Empty));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        Process process,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new EndOfStreamException("The MCP host closed stdout before returning a response.");
            }

            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == expectedId)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VsDebugMcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}