using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class Phase8eProtocolTests
{
    [Fact]
    public void DebuggerSwitchThreadRequestAndResponse_RoundTrips()
    {
        var request = new DebuggerSwitchThreadRequest
        {
            ThreadId = 30,
            VsInstanceId = "instance-1"
        };

        var json = BridgeJson.Serialize(request);
        var reqDeserialized = BridgeJson.Deserialize<DebuggerSwitchThreadRequest>(json);
        Assert.Equal(30, reqDeserialized.ThreadId);
        Assert.Equal("instance-1", reqDeserialized.VsInstanceId);

        var response = new DebuggerSwitchThreadResponse
        {
            VsInstanceId = "instance-1",
            PreviousThreadId = 0,
            CurrentThreadId = 30,
            ThreadName = "QtWorkerThread",
            IsAlive = true,
            SuspendCount = 0,
            TopFrame = "PrintConfigDll.dll!QEventLoop::exec"
        };

        var respJson = BridgeJson.Serialize(response);
        var respDeserialized = BridgeJson.Deserialize<DebuggerSwitchThreadResponse>(respJson);
        Assert.Equal(30, respDeserialized.CurrentThreadId);
        Assert.Equal("QtWorkerThread", respDeserialized.ThreadName);
        Assert.True(respDeserialized.IsAlive);
        Assert.Equal("PrintConfigDll.dll!QEventLoop::exec", respDeserialized.TopFrame);
    }

    [Fact]
    public void DebuggerGetAllThreadsCallStacks_RoundTrips_WithDeadlockWarnings()
    {
        var request = new DebuggerGetAllThreadsCallStacksRequest
        {
            MaxFramesPerThread = 10,
            UserCodeOnly = true,
            VsInstanceId = "instance-1"
        };

        var reqJson = BridgeJson.Serialize(request);
        var reqDeserialized = BridgeJson.Deserialize<DebuggerGetAllThreadsCallStacksRequest>(reqJson);
        Assert.Equal(10, reqDeserialized.MaxFramesPerThread);
        Assert.True(reqDeserialized.UserCodeOnly);

        var response = new DebuggerGetAllThreadsCallStacksResponse
        {
            VsInstanceId = "instance-1",
            CurrentThreadId = 1,
            TotalThreads = 2,
            DeadlockWarnings = new List<string>
            {
                "Potential cross-framework modal event loop hang detected: Thread(s) [30] running nested Qt exec() loop while 1 thread(s) wait on synchronization primitives."
            },
            Threads = new List<ThreadCallStackSummary>
            {
                new()
                {
                    ThreadId = 1,
                    Name = "Main UI Thread",
                    IsAlive = true,
                    TopFrame = "ntdll.dll!NtWaitForMultipleObjects",
                    WaitingHeuristic = "Kernel Synchronization Wait (Event / Semaphore / Mutex)"
                },
                new()
                {
                    ThreadId = 30,
                    Name = "LabVIEW Worker Thread",
                    IsAlive = true,
                    TopFrame = "Qt5Core.dll!QEventLoop::exec",
                    WaitingHeuristic = "Nested Qt Modal / Event Loop (Hang / Modal Deadlock Risk)",
                    FirstUserFrame = "PrintConfigDll.dll!SystemMaintenance::SystemMaintenance"
                }
            }
        };

        var respJson = BridgeJson.Serialize(response);
        var respDeserialized = BridgeJson.Deserialize<DebuggerGetAllThreadsCallStacksResponse>(respJson);
        Assert.Equal(2, respDeserialized.TotalThreads);
        Assert.Single(respDeserialized.DeadlockWarnings);
        Assert.Contains("nested Qt exec()", respDeserialized.DeadlockWarnings[0]);
        Assert.Equal("Main UI Thread", respDeserialized.Threads[0].Name);
        Assert.Equal("Nested Qt Modal / Event Loop (Hang / Modal Deadlock Risk)", respDeserialized.Threads[1].WaitingHeuristic);
    }

    [Fact]
    public void DebuggerReadMemoryTyped_RoundTrips_WithPointerChain()
    {
        var request = new DebuggerReadMemoryTypedRequest
        {
            Address = "PrintConfigDll!g_ptr->+0x18->+0x8",
            Type = "pointer_chain",
            PointerSize = 8,
            ThreadId = 30,
            VsInstanceId = "instance-1"
        };

        var reqJson = BridgeJson.Serialize(request);
        var reqDeserialized = BridgeJson.Deserialize<DebuggerReadMemoryTypedRequest>(reqJson);
        Assert.Equal("PrintConfigDll!g_ptr->+0x18->+0x8", reqDeserialized.Address);
        Assert.Equal("pointer_chain", reqDeserialized.Type);
        Assert.Equal(30, reqDeserialized.ThreadId);

        var response = new DebuggerReadMemoryTypedResponse
        {
            VsInstanceId = "instance-1",
            Address = request.Address,
            ResolvedAddress = "0x00007FFB12345678",
            Type = "pointer_chain",
            PointerChainValues = new List<string>
            {
                "[Base: PrintConfigDll!g_ptr] = 0x00007FFB10002000",
                "-> *0x00007FFB10002000 = 0x00007FFB20000000 (+0x18) => 0x00007FFB20000018",
                "-> *0x00007FFB20000018 = 0x00007FFB12345670 (+0x8) => 0x00007FFB12345678"
            },
            BytesRead = 8,
            ReadSource = "win32_read_process_memory"
        };

        var respJson = BridgeJson.Serialize(response);
        var respDeserialized = BridgeJson.Deserialize<DebuggerReadMemoryTypedResponse>(respJson);
        Assert.Equal("0x00007FFB12345678", respDeserialized.ResolvedAddress);
        Assert.Equal(3, respDeserialized.PointerChainValues?.Count);
        Assert.Equal("win32_read_process_memory", respDeserialized.ReadSource);
    }

    [Fact]
    public void ThreadId_RoundTrips_InExistingInspectionRequests()
    {
        var localsReq = new DebuggerGetLocalsRequest
        {
            FrameIndex = 2,
            MaxCount = 20,
            ThreadId = 30
        };
        var localsDeserialized = BridgeJson.Deserialize<DebuggerGetLocalsRequest>(BridgeJson.Serialize(localsReq));
        Assert.Equal(30, localsDeserialized.ThreadId);

        var evalReq = new DebuggerEvaluateExprRequest
        {
            Expression = "m_stationNo",
            ThreadId = 30,
            FrameIndex = 1
        };
        var evalDeserialized = BridgeJson.Deserialize<DebuggerEvaluateExprRequest>(BridgeJson.Serialize(evalReq));
        Assert.Equal(30, evalDeserialized.ThreadId);

        var disasmReq = new DebuggerGetDisassemblyRequest
        {
            Address = "0x7FFB1234",
            ThreadId = 30
        };
        var disasmDeserialized = BridgeJson.Deserialize<DebuggerGetDisassemblyRequest>(BridgeJson.Serialize(disasmReq));
        Assert.Equal(30, disasmDeserialized.ThreadId);
    }

    [Theory]
    [InlineData("PrintConfigDll!print+0x4aa2a", "PrintConfigDll", "print", "0x4aa2a")]
    [InlineData("PrintConfigDll.dll!print+0x4aa2a", "PrintConfigDll.dll", "print", "0x4aa2a")]
    [InlineData("PrintConfigDll!+0x4aa2a", "PrintConfigDll", null, "0x4aa2a")]
    [InlineData("PrintConfigDll+0x4aa2a", "PrintConfigDll", null, "0x4aa2a")]
    [InlineData("PrintConfigDll.dll+0x4aa2a", "PrintConfigDll.dll", null, "0x4aa2a")]
    [InlineData("kernel32!Sleep", "kernel32", "Sleep", null)]
    public void WinDbgSyntaxPattern_MatchesAccurately(string input, string expectedMod, string? expectedSymbol, string? expectedOffset)
    {
        var bangMatch = Regex.Match(input, @"^([a-zA-Z0-9_\.-]+)!(?:([a-zA-Z0-9_:]+))?(?:\+(0x[0-9a-fA-F]+|[0-9a-fA-F]+))?$");
        var plusMatch = !bangMatch.Success ? Regex.Match(input, @"^([a-zA-Z0-9_\.-]+)\+(0x[0-9a-fA-F]+|[0-9a-fA-F]+)$") : null;

        Assert.True(bangMatch.Success || (plusMatch != null && plusMatch.Success));

        string targetMod;
        string? targetSymbol = null;
        string? targetOffsetStr;

        if (bangMatch.Success)
        {
            targetMod = bangMatch.Groups[1].Value;
            if (bangMatch.Groups[2].Success && !string.IsNullOrEmpty(bangMatch.Groups[2].Value))
            {
                targetSymbol = bangMatch.Groups[2].Value;
            }
            targetOffsetStr = bangMatch.Groups[3].Success ? bangMatch.Groups[3].Value : null;
        }
        else
        {
            targetMod = plusMatch!.Groups[1].Value;
            targetOffsetStr = plusMatch.Groups[2].Value;
        }

        Assert.Equal(expectedMod, targetMod);
        Assert.Equal(expectedSymbol, targetSymbol);
        Assert.Equal(expectedOffset, targetOffsetStr);
    }
}
