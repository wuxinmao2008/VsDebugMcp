using System.IO;
using System.Linq;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class CMakePresetsTests
{
    private const string SamplePresetsJson = """
    {
      "version": 3,
      "configurePresets": [
        {
          "name": "windows-base",
          "hidden": true,
          "generator": "Ninja",
          "binaryDir": "${sourceDir}/out/build/${presetName}"
        },
        {
          "name": "x64-Debug",
          "displayName": "x64 Debug",
          "description": "Target Windows 64-bit Debug",
          "inherits": "windows-base",
          "architecture": {
            "value": "x64",
            "strategy": "external"
          },
          "cacheVariables": {
            "CMAKE_BUILD_TYPE": "Debug"
          }
        },
        {
          "name": "x64-Release",
          "displayName": "x64 Release",
          "description": "Target Windows 64-bit Release",
          "architecture": "x64",
          "cacheVariables": {
            "CMAKE_BUILD_TYPE": "Release"
          }
        }
      ]
    }
    """;

    [Fact]
    public void ParseConfigurePresets_FiltersHiddenAndParsesVisiblePresets()
    {
        var presets = CMakePresetsParser.ParseConfigurePresets(SamplePresetsJson, "D:/project");

        Assert.Equal(2, presets.Count);

        var debugPreset = presets.FirstOrDefault(p => p.Name == "x64-Debug");
        Assert.NotNull(debugPreset);
        Assert.Equal("x64 Debug", debugPreset.DisplayName);
        Assert.Equal("x64", debugPreset.Architecture);
        Assert.Equal("Debug", debugPreset.BuildType);

        var releasePreset = presets.FirstOrDefault(p => p.Name == "x64-Release");
        Assert.NotNull(releasePreset);
        Assert.Equal("x64 Release", releasePreset.DisplayName);
        Assert.Equal("x64", releasePreset.Architecture);
        Assert.Equal("Release", releasePreset.BuildType);
    }

    [Fact]
    public void ParseConfigurePresets_EvaluatesBinaryDir()
    {
        var json = """
        {
          "version": 3,
          "configurePresets": [
            {
              "name": "linux-dbg",
              "binaryDir": "${sourceDir}/build/${presetName}"
            }
          ]
        }
        """;

        var presets = CMakePresetsParser.ParseConfigurePresets(json, "C:/MyWorkspace");
        var preset = Assert.Single(presets);
        Assert.Equal("C:/MyWorkspace/build/linux-dbg", preset.BinaryDir);
    }

    [Fact]
    public void ParseConfigurePresets_InvalidJson_ReturnsEmptyList()
    {
        var presets = CMakePresetsParser.ParseConfigurePresets("not valid json", "C:/Workspace");
        Assert.Empty(presets);
    }

    [Fact]
    public void LoadWorkspacePresets_FromRealSample()
    {
        var samplePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\sample\SampleCMake"));
        if (Directory.Exists(samplePath))
        {
            var presets = CMakePresetsParser.LoadWorkspacePresets(samplePath);
            Assert.Contains(presets, p => p.Name == "x64-Debug");
            Assert.Contains(presets, p => p.Name == "x64-Release");
            Assert.DoesNotContain(presets, p => p.Name == "windows-base");
        }
    }
}
