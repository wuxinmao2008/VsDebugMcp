using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using VsDebugMcp.Protocol;
using Xunit;

namespace VsDebugMcp.Protocol.Tests;

public sealed class MinidumpParserTests
{
    [Fact]
    public void IsMinidumpFile_DetectsExistingDmpFiles()
    {
        var tempDmp = Path.GetTempFileName() + ".dmp";
        var tempExe = Path.GetTempFileName() + ".exe";
        try
        {
            File.WriteAllBytes(tempDmp, new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(tempExe, new byte[] { 1, 2, 3, 4 });

            Assert.True(MinidumpParser.IsMinidumpFile(tempDmp));
            Assert.False(MinidumpParser.IsMinidumpFile(tempExe));
            Assert.False(MinidumpParser.IsMinidumpFile(@"C:\non_existent_12345.dmp"));
            Assert.False(MinidumpParser.IsMinidumpFile(string.Empty));
            Assert.False(MinidumpParser.IsMinidumpFile(null));
        }
        finally
        {
            if (File.Exists(tempDmp)) File.Delete(tempDmp);
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }

    [Fact]
    public void TryReadExceptionInfo_SyntheticMinidump_ParsesAccessViolationDetails()
    {
        var tempFile = Path.GetTempFileName() + ".dmp";
        try
        {
            WriteSyntheticMinidumpWithException(
                tempFile,
                threadId: 3948,
                exceptionCode: 0xC0000005u,
                exceptionFlags: 0u,
                exceptionAddress: 0x00007FFA7BAFCBA2UL,
                accessFlag: 0UL, // 0 = read
                faultingAddress: 0xFFFFFFFFFFFFFFFFUL);

            var ok = MinidumpParser.TryReadExceptionInfo(tempFile, out var info);
            Assert.True(ok);
            Assert.NotNull(info);
            Assert.Equal(3948, info!.ThreadId);
            Assert.Equal(0xC0000005u, info.ExceptionCode);
            Assert.Equal("0xC0000005", info.ExceptionCodeHex);
            Assert.Equal("EXCEPTION_ACCESS_VIOLATION", info.ExceptionName);
            Assert.Equal(0x00007FFA7BAFCBA2UL, info.ExceptionAddress);
            Assert.Equal("0x00007FFA7BAFCBA2", info.ExceptionAddressHex);
            Assert.Equal("read", info.AccessOperation);
            Assert.Equal(0xFFFFFFFFFFFFFFFFUL, info.FaultingAddress);
            Assert.Equal("0xFFFFFFFFFFFFFFFF", info.FaultingAddressHex);
            Assert.Contains("Access violation reading location 0xFFFFFFFFFFFFFFFF", info.FormattedMessage);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void TryReadMemory_Memory64ListStream_ReadsTargetAddressAndUtf16Preview()
    {
        var tempFile = Path.GetTempFileName() + ".dmp";
        try
        {
            const ulong regionBase = 0x0000018125E00000UL;
            const ulong targetAddress = 0x0000018125E00020UL;
            var payloadText = @"D:\Program Files\Demo\config.ini";
            var utf16Bytes = Encoding.Unicode.GetBytes(payloadText + "\0");
            var regionBytes = new byte[128];
            Array.Copy(utf16Bytes, 0, regionBytes, (int)(targetAddress - regionBase), utf16Bytes.Length);

            WriteSyntheticMinidumpWithMemory64(tempFile, regionBase, regionBytes);

            var ok = MinidumpParser.TryReadMemory(tempFile, targetAddress, 64, out var readBytes);
            Assert.True(ok);
            Assert.NotNull(readBytes);
            Assert.Equal(64, readBytes.Length);

            var preview = MinidumpParser.ExtractUtf16Preview(readBytes);
            Assert.Equal(payloadText, preview);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void TryReadMemory_MemoryListStream_ReadsTargetAddress()
    {
        var tempFile = Path.GetTempFileName() + ".dmp";
        try
        {
            const ulong regionBase = 0x0000004EFB6FC000UL;
            var regionBytes = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
            WriteSyntheticMinidumpWithMemoryList(tempFile, regionBase, regionBytes);

            var ok = MinidumpParser.TryReadMemory(tempFile, regionBase + 2, 4, out var readBytes);
            Assert.True(ok);
            Assert.NotNull(readBytes);
            Assert.Equal(new byte[] { 0x30, 0x40, 0x50, 0x60 }, readBytes);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void TryReadPeImageBase_SyntheticPe64_ReturnsExpectedImageBase()
    {
        var tempFile = Path.GetTempFileName() + ".dll";
        try
        {
            var peBytes = new byte[256];
            peBytes[0] = (byte)'M';
            peBytes[1] = (byte)'Z';
            var eLfanew = BitConverter.GetBytes(0x80);
            Array.Copy(eLfanew, 0, peBytes, 0x3C, 4);
            peBytes[0x80] = (byte)'P';
            peBytes[0x81] = (byte)'E';
            peBytes[0x82] = 0;
            peBytes[0x83] = 0;
            // Optional header starts at 0x80 + 4 + 20 = 0x98
            var magic = BitConverter.GetBytes((ushort)0x20B); // PE32+
            Array.Copy(magic, 0, peBytes, 0x98, 2);
            var imgBaseBytes = BitConverter.GetBytes(0x0000000180000000UL);
            Array.Copy(imgBaseBytes, 0, peBytes, 0x98 + 24, 8);
            File.WriteAllBytes(tempFile, peBytes);

            var ok = MinidumpParser.TryReadPeImageBase(tempFile, out var imageBase);
            Assert.True(ok);
            Assert.Equal(0x0000000180000000UL, imageBase);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ParseAndRebaseDumpbinOutput_RebasesAndMarksTargetInstruction()
    {
        var sampleOutput = string.Join(Environment.NewLine, new[]
        {
            "Microsoft (R) COFF/PE Dumper Version 14.50.35728.0",
            "  000000018000CB8B: CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC  ................",
            "  000000018000CB9B: 48 83 C9 FF        or          rcx,0FFFFFFFFFFFFFFFFh",
            "  000000018000CB9F: 45 33 C0           xor         r8d,r8d",
            "  000000018000CBA2: 66 44 39 44 4A 02  cmp         word ptr [rdx+rcx*2+2],r8w",
            "  000000018000CBA8: 75 F5              jne         000000018000CB9F"
        });

        const ulong preferredImageBase = 0x0000000180000000UL;
        const ulong runtimeLoadBase = 0x00007FFA7BAF0000UL;
        const ulong targetAddress = 0x00007FFA7BAFCBA2UL;

        var instructions = MinidumpParser.ParseAndRebaseDumpbinOutput(
            sampleOutput,
            targetRuntimeAddress: targetAddress,
            moduleLoadAddress: runtimeLoadBase,
            peImageBase: preferredImageBase,
            instructionCount: 4,
            instructionsBefore: 2);

        Assert.Equal(4, instructions.Count);
        Assert.Equal("0x00007FFA7BAFCB9B", instructions[0].Address);
        Assert.False(instructions[0].IsCurrentInstruction);

        Assert.Equal("0x00007FFA7BAFCBA2", instructions[2].Address);
        Assert.Equal("66 44 39 44 4A 02", instructions[2].Bytes);
        Assert.Contains("cmp", instructions[2].Instruction);
        Assert.Contains("word ptr [rdx+rcx*2+2],r8w", instructions[2].Instruction);
        Assert.True(instructions[2].IsCurrentInstruction);

        var formatted = MinidumpParser.FormatDisassemblyText(instructions);
        Assert.Contains(">>> 0x00007FFA7BAFCBA2:", formatted);
    }

    [Fact]
    public void SampleCMakeCrashDump_WhenPresent_ParsesAccessViolationDetails()
    {
        var sampleDmp = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\sample\SampleCMake\out\build\x64-Debug\src\app\crash_sample.dmp"));
        if (!File.Exists(sampleDmp))
        {
            return;
        }

        var okExc = MinidumpParser.TryReadExceptionInfo(sampleDmp, out var excInfo);
        Assert.True(okExc);
        Assert.NotNull(excInfo);
        Assert.Equal("0xC0000005", excInfo!.ExceptionCodeHex);
        Assert.Equal("EXCEPTION_ACCESS_VIOLATION", excInfo.ExceptionName);
        Assert.Equal("read", excInfo.AccessOperation);
        Assert.Equal("0x00000000DEACFFFE", excInfo.FaultingAddressHex);
        Assert.Contains("Access violation reading location 0x00000000DEACFFFE", excInfo.FormattedMessage);
    }

    [Fact]
    public void Phase8BContracts_RoundTripJsonSerialization()
    {
        var disasmResponse = new DebuggerGetDisassemblyResponse
        {
            VsInstanceId = "vs-1",
            TargetAddress = "0x00007FFA7BAFCBA2",
            ModuleName = "PrintConfigDll.dll",
            FunctionName = "PrintConfigDll.dll!0x00007FFA7BAFCBA2",
            FormattedText = ">>> 0x00007FFA7BAFCBA2: 66 44 39 44 4A 02  cmp word ptr [rdx+rcx*2+2],r8w",
            Instructions = new List<DisassemblyInstruction>
            {
                new DisassemblyInstruction
                {
                    Address = "0x00007FFA7BAFCBA2",
                    Bytes = "66 44 39 44 4A 02",
                    Instruction = "cmp word ptr [rdx+rcx*2+2],r8w",
                    IsCurrentInstruction = true
                }
            }
        };

        var json = JsonConvert.SerializeObject(disasmResponse);
        var roundTripped = JsonConvert.DeserializeObject<DebuggerGetDisassemblyResponse>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal("PrintConfigDll.dll", roundTripped!.ModuleName);
        Assert.Single(roundTripped.Instructions);
        Assert.True(roundTripped.Instructions[0].IsCurrentInstruction);
    }

    [Fact]
    public void DisassembleMemoryBytes_IcedEngine_DisassemblesX64BytesCorrectly()
    {
        // 0F B7 04 41 : movzx eax, word ptr [rcx + rax*2]
        // 48 83 C4 10 : add rsp, 10h
        // C3          : ret
        var codeBytes = new byte[] { 0x0F, 0xB7, 0x04, 0x41, 0x48, 0x83, 0xC4, 0x10, 0xC3 };
        const ulong baseAddress = 0x00007FF6FB8B1DBD;

        var instructions = MinidumpParser.DisassembleMemoryBytes(
            codeBytes,
            baseAddress,
            bitness: 64,
            currentInstructionAddress: baseAddress,
            maxInstructions: 5);

        Assert.Equal(3, instructions.Count);
        Assert.Equal("0x00007FF6FB8B1DBD", instructions[0].Address);
        Assert.True(instructions[0].IsCurrentInstruction);
        Assert.Contains("movzx", instructions[0].Instruction);
        Assert.Contains("0F B7 04 41", instructions[0].Bytes);

        Assert.Equal("0x00007FF6FB8B1DC1", instructions[1].Address);
        Assert.False(instructions[1].IsCurrentInstruction);
        Assert.Contains("add", instructions[1].Instruction);

        Assert.Equal("0x00007FF6FB8B1DC5", instructions[2].Address);
        Assert.Contains("ret", instructions[2].Instruction);
    }

    [Fact]
    public void SampleCMakeCrashDump_WhenPresent_ExtractsRegistersContext()
    {
        var sampleDmp = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\sample\SampleCMake\out\build\x64-Debug\src\app\crash_sample.dmp"));
        if (!File.Exists(sampleDmp))
        {
            return;
        }

        var ok = MinidumpParser.TryReadThreadContext(sampleDmp, targetThreadId: null, out var ctx);
        Assert.True(ok);
        Assert.NotNull(ctx);
        Assert.Equal(6428, ctx!.ThreadId);
        Assert.Equal("x64", ctx.Architecture);
        Assert.Equal("0xFFFFFFFFFFFFFFFF", ctx.Registers["RAX"]);
        Assert.Equal("0x00000000DEAD0000", ctx.Registers["RCX"]);
        Assert.Equal("0x00007FF6FB8B1DBD", ctx.InstructionPointer);
        Assert.NotEmpty(ctx.DetailedRegisters);
        Assert.True(ctx.DetailedRegisters.Count >= 17);
    }

    [Fact]
    public void SampleCMakeCrashDump_WhenPresent_ExtractsCrashSummaryWithDisassembly()
    {
        var sampleDmp = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\sample\SampleCMake\out\build\x64-Debug\src\app\crash_sample.dmp"));
        if (!File.Exists(sampleDmp))
        {
            return;
        }

        var ok = MinidumpParser.TryReadCrashSummary(sampleDmp, out var summary);
        Assert.True(ok);
        Assert.NotNull(summary);
        Assert.Equal(6428, summary!.ThreadId);
        Assert.Equal("0xC0000005", summary.ExceptionCodeHex);
        Assert.Equal("0x00007FF6FB8B1DBD", summary.ExceptionAddressHex);
        Assert.Equal("read", summary.AccessOperation);
        Assert.Equal("0x00000000DEACFFFE", summary.FaultingAddressHex);
        Assert.NotNull(summary.FaultingInstructionDisassembly);
        Assert.Contains("movzx", summary.FaultingInstructionDisassembly!);
        Assert.Contains("=== Minidump Crash Summary ===", summary.FormattedSummary);
        Assert.Contains("0xFFFFFFFFFFFFFFFF", summary.Registers["RAX"]);
    }

    [Fact]
    public void SampleCMakeCrashDump_WhenPresent_QueriesMemoryInDump()
    {
        var sampleDmp = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\sample\SampleCMake\out\build\x64-Debug\src\app\crash_sample.dmp"));
        if (!File.Exists(sampleDmp))
        {
            return;
        }

        var okTarget = MinidumpParser.TryQueryMemoryInDump(sampleDmp, 0x00007FF6FB8B1DBD, out var memInfo);
        Assert.True(okTarget);
        Assert.NotNull(memInfo);
        Assert.True(memInfo!.IsPresentInDump);
        Assert.Equal("Memory64ListStream", memInfo.StreamSource);

        var okMissing = MinidumpParser.TryQueryMemoryInDump(sampleDmp, 0x0000000000001000, out var missingInfo);
        Assert.True(okMissing);
        Assert.NotNull(missingInfo);
        Assert.False(missingInfo!.IsPresentInDump);
    }

    [Fact]
    public void Phase8DContracts_RoundTripJsonSerialization()
    {
        var regResponse = new DebuggerGetRegistersResponse
        {
            VsInstanceId = "vs-1",
            ThreadId = 1234,
            Architecture = "x64",
            Registers = new Dictionary<string, string> { ["RAX"] = "0x1", ["RIP"] = "0x7FFE" },
            InstructionPointer = "0x7FFE",
            StackPointer = "0x1000",
            FlagsHex = "0x202",
            FlagBits = new Dictionary<string, bool> { ["ZF"] = false, ["CF"] = true }
        };
        var regJson = JsonConvert.SerializeObject(regResponse);
        var regRt = JsonConvert.DeserializeObject<DebuggerGetRegistersResponse>(regJson);
        Assert.NotNull(regRt);
        Assert.Equal(1234, regRt!.ThreadId);
        Assert.Equal("0x7FFE", regRt.InstructionPointer);

        var summaryResponse = new DebuggerGetCrashSummaryResponse
        {
            VsInstanceId = "vs-1",
            DumpPath = @"C:\crashes\demo.dmp",
            FaultingThreadId = 1234,
            ExceptionCodeHex = "0xC0000005",
            ExceptionName = "EXCEPTION_ACCESS_VIOLATION",
            FormattedSummary = "Sample crash",
            FaultingInstructionDisassembly = ">>> 0x7FFE: mov rax, [rcx]"
        };
        var sumJson = JsonConvert.SerializeObject(summaryResponse);
        var sumRt = JsonConvert.DeserializeObject<DebuggerGetCrashSummaryResponse>(sumJson);
        Assert.NotNull(sumRt);
        Assert.Equal("0xC0000005", sumRt!.ExceptionCodeHex);

        var openResponse = new DebuggerOpenDumpResponse
        {
            VsInstanceId = "vs-1",
            DumpPath = @"C:\crashes\demo.dmp",
            Success = true,
            Message = "Opened dump successfully"
        };
        var openJson = JsonConvert.SerializeObject(openResponse);
        var openRt = JsonConvert.DeserializeObject<DebuggerOpenDumpResponse>(openJson);
        Assert.NotNull(openRt);
        Assert.True(openRt!.Success);

        var ptrResponse = new DebuggerDumpPointersResponse
        {
            VsInstanceId = "vs-1",
            BaseAddress = "0x1000",
            Count = 2,
            PointerSize = 8,
            Pointers = new List<PointerDumpEntry>
            {
                new() { Address = "0x1000", PointerValue = "0x7FFE", PointerValue64 = 0x7FFE, ModuleName = "app.exe" }
            }
        };
        var ptrJson = JsonConvert.SerializeObject(ptrResponse);
        var ptrRt = JsonConvert.DeserializeObject<DebuggerDumpPointersResponse>(ptrJson);
        Assert.NotNull(ptrRt);
        Assert.Single(ptrRt!.Pointers);

        var queryResponse = new DebuggerQueryMemoryResponse
        {
            VsInstanceId = "vs-1",
            Address = "0x1000",
            IsValid = true,
            State = "MEM_COMMIT",
            Protect = "PAGE_READWRITE",
            Type = "MEM_PRIVATE"
        };
        var qJson = JsonConvert.SerializeObject(queryResponse);
        var qRt = JsonConvert.DeserializeObject<DebuggerQueryMemoryResponse>(qJson);
        Assert.NotNull(qRt);
        Assert.Equal("MEM_COMMIT", qRt!.State);
    }

    private static void WriteSyntheticMinidumpWithException(
        string path,
        uint threadId,
        uint exceptionCode,
        uint exceptionFlags,
        ulong exceptionAddress,
        ulong accessFlag,
        ulong faultingAddress)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // Header (32 bytes): Signature("MDMP"), Version, NumberOfStreams=1, StreamDirectoryRva=32
        bw.Write(0x504D444Du);
        bw.Write(0x0000A793u);
        bw.Write(1u);
        bw.Write(32u);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0UL);

        // Directory entry at offset 32 (12 bytes): StreamType=6 (ExceptionStream), DataSize=168, Rva=44
        bw.Write(6u);
        bw.Write(168u);
        bw.Write(44u);

        // MINIDUMP_EXCEPTION_STREAM at offset 44:
        bw.Write(threadId);
        bw.Write(0u); // __alignment
        // MINIDUMP_EXCEPTION:
        bw.Write(exceptionCode);
        bw.Write(exceptionFlags);
        bw.Write(0UL); // ExceptionRecord
        bw.Write(exceptionAddress);
        bw.Write(2u); // NumberParameters = 2
        bw.Write(0u); // __unusedAlignment
        bw.Write(accessFlag);
        bw.Write(faultingAddress);
        for (var i = 2; i < 15; i++)
        {
            bw.Write(0UL);
        }
        // ThreadContext descriptor (8 bytes)
        bw.Write(0u);
        bw.Write(0u);
    }

    private static void WriteSyntheticMinidumpWithMemory64(string path, ulong baseAddress, byte[] memoryData)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // Header (32 bytes): NumberOfStreams=1, StreamDirectoryRva=32
        bw.Write(0x504D444Du);
        bw.Write(0x0000A793u);
        bw.Write(1u);
        bw.Write(32u);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0UL);

        // Directory entry at offset 32 (12 bytes): StreamType=9 (Memory64ListStream), DataSize=32, Rva=44
        bw.Write(9u);
        bw.Write(32u);
        bw.Write(44u);

        // MINIDUMP_MEMORY64_LIST at offset 44:
        // NumberOfMemoryRanges (ulong) = 1, BaseRva (ulong) = 76
        bw.Write(1UL);
        bw.Write(76UL);
        // Descriptor 0: StartOfMemoryRange (ulong), DataSize (ulong)
        bw.Write(baseAddress);
        bw.Write((ulong)memoryData.Length);

        // Raw memory at offset 76:
        bw.Write(memoryData);
    }

    private static void WriteSyntheticMinidumpWithMemoryList(string path, ulong baseAddress, byte[] memoryData)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // Header (32 bytes)
        bw.Write(0x504D444Du);
        bw.Write(0x0000A793u);
        bw.Write(1u);
        bw.Write(32u);
        bw.Write(0u);
        bw.Write(0u);
        bw.Write(0UL);

        // Directory entry at offset 32 (12 bytes): StreamType=5 (MemoryListStream), DataSize=20, Rva=44
        bw.Write(5u);
        bw.Write(20u);
        bw.Write(44u);

        // MINIDUMP_MEMORY_LIST at offset 44:
        // NumberOfMemoryRanges (uint) = 1
        bw.Write(1u);
        // Descriptor 0 (16 bytes): StartOfMemoryRange (ulong), DataSize (uint), Rva (uint = 64)
        bw.Write(baseAddress);
        bw.Write((uint)memoryData.Length);
        bw.Write(64u);

        // Raw memory at offset 64:
        bw.Write(memoryData);
    }
}
