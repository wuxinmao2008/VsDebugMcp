using System;
using System.IO;
using System.Linq;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class CTestParserTests
{
    private const string SampleDiscoveryJson = """
    {
      "backtraceGraph" : 
      {
        "commands" : [ "add_test" ],
        "files" : [ "D:/VsDebugMcp/sample/SampleCMake/tests/CMakeLists.txt" ],
        "nodes" : 
        [
          { "file" : 0 },
          { "command" : 0, "file" : 0, "line" : 12, "parent" : 0 }
        ]
      },
      "kind" : "ctestInfo",
      "tests" : 
      [
        {
          "backtrace" : 1,
          "command" : 
          [
            "D:/VsDebugMcp/sample/SampleCMake/out/build/x64-Debug/tests/sample_tests.exe"
          ],
          "name" : "MathOperationsTest",
          "properties" : 
          [
            {
              "name" : "WORKING_DIRECTORY",
              "value" : "D:/VsDebugMcp/sample/SampleCMake/out/build/x64-Debug/tests"
            }
          ]
        }
      ],
      "version" : { "major" : 1, "minor" : 0 }
    }
    """;

    private const string SampleJUnitXml = """
    <?xml version="1.0" encoding="UTF-8"?>
    <testsuite name="(empty)" tests="2" failures="1" disabled="0" skipped="0" hostname="" time="0.05" timestamp="2026-09-15T14:30:03">
      <testcase name="TestSuccess" classname="TestSuccess" time="0.02" status="run">
        <properties/>
        <system-out>Passed output</system-out>
      </testcase>
      <testcase name="TestFailure" classname="TestFailure" time="0.03" status="run">
        <properties/>
        <failure message="Assertion failed in test.cpp:42">Stack trace here</failure>
        <system-out>Failed output</system-out>
      </testcase>
    </testsuite>
    """;

    [Fact]
    public void ParseDiscoveryJson_ParsesTestsWithBacktrace()
    {
        var items = CTestParser.ParseDiscoveryJson(SampleDiscoveryJson);
        var test = Assert.Single(items);

        Assert.Equal("MathOperationsTest", test.Name);
        Assert.Equal("D:/VsDebugMcp/sample/SampleCMake/out/build/x64-Debug/tests/sample_tests.exe", test.Command);
        Assert.Equal("D:/VsDebugMcp/sample/SampleCMake/out/build/x64-Debug/tests", test.WorkingDirectory);
        Assert.Equal("D:/VsDebugMcp/sample/SampleCMake/tests/CMakeLists.txt", test.FilePath);
        Assert.Equal(12, test.LineNumber);
    }

    [Fact]
    public void ParseDiscoveryJson_EmptyOrInvalid_ReturnsEmptyList()
    {
        var items1 = CTestParser.ParseDiscoveryJson("");
        Assert.Empty(items1);

        var items2 = CTestParser.ParseDiscoveryJson("invalid json");
        Assert.Empty(items2);
    }

    [Fact]
    public void ParseJUnitXml_ParsesPassedAndFailedTests()
    {
        var result = CTestParser.ParseJUnitXml(SampleJUnitXml);

        Assert.Equal(2, result.TotalTests);
        Assert.Equal(1, result.PassedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);

        Assert.True(result.TestOutcomes.ContainsKey("TestSuccess"));
        var success = result.TestOutcomes["TestSuccess"];
        Assert.Equal("Passed", success.State);
        Assert.Equal(20, success.DurationMs);
        Assert.Equal("Passed output", success.SystemOut);

        Assert.True(result.TestOutcomes.ContainsKey("TestFailure"));
        var fail = result.TestOutcomes["TestFailure"];
        Assert.Equal("Failed", fail.State);
        Assert.Equal(30, fail.DurationMs);
        Assert.Equal("Assertion failed in test.cpp:42", fail.ErrorMessage);
    }

    [Fact]
    public void CMakePresets_ResolvesActualWorkspacePresets()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
        var sampleDir = Path.Combine(repoRoot, "sample", "SampleCMake");
        var presets = CMakePresetsParser.LoadWorkspacePresets(sampleDir);
        Assert.NotEmpty(presets);
        var debugPreset = presets.FirstOrDefault(p => p.Name == "x64-Debug");
        Assert.NotNull(debugPreset);
        Assert.True(Directory.Exists(debugPreset.BinaryDir), $"binaryDir does not exist: '{debugPreset.BinaryDir}'");
    }
}
