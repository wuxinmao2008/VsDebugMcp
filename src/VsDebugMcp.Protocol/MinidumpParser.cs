using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Iced.Intel;

namespace VsDebugMcp.Protocol;

public sealed class MinidumpExceptionInfo
{
    public int ThreadId { get; set; }
    public uint ExceptionCode { get; set; }
    public string ExceptionCodeHex { get; set; } = string.Empty;
    public string ExceptionName { get; set; } = string.Empty;
    public ulong ExceptionAddress { get; set; }
    public string ExceptionAddressHex { get; set; } = string.Empty;
    public string? AccessOperation { get; set; }
    public ulong? FaultingAddress { get; set; }
    public string? FaultingAddressHex { get; set; }
    public string FormattedMessage { get; set; } = string.Empty;
}

public sealed class CpuContextInfo
{
    public int ThreadId { get; set; }
    public string Architecture { get; set; } = "x64";
    public Dictionary<string, string> Registers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RegisterValueInfo> DetailedRegisters { get; set; } = new();
    public string InstructionPointer { get; set; } = string.Empty;
    public ulong InstructionPointer64 { get; set; }
    public string StackPointer { get; set; } = string.Empty;
    public ulong StackPointer64 { get; set; }
    public string? FramePointer { get; set; }
    public ulong? FramePointer64 { get; set; }
    public string FlagsHex { get; set; } = string.Empty;
    public Dictionary<string, bool> FlagBits { get; set; } = new();
}

public sealed class MinidumpCrashSummary
{
    public int ThreadId { get; set; }
    public string ExceptionCodeHex { get; set; } = string.Empty;
    public string ExceptionName { get; set; } = string.Empty;
    public string ExceptionAddressHex { get; set; } = string.Empty;
    public ulong ExceptionAddress { get; set; }
    public string? AccessOperation { get; set; }
    public string? FaultingAddressHex { get; set; }
    public ulong? FaultingAddress { get; set; }
    public Dictionary<string, string> Registers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? FaultingInstructionDisassembly { get; set; }
    public string FormattedSummary { get; set; } = string.Empty;
}

public sealed class MinidumpMemoryQueryInfo
{
    public ulong Address { get; set; }
    public string AddressHex { get; set; } = string.Empty;
    public bool IsPresentInDump { get; set; }
    public ulong BaseAddress { get; set; }
    public string BaseAddressHex { get; set; } = string.Empty;
    public ulong RegionSize { get; set; }
    public string RegionSizeHex { get; set; } = string.Empty;
    public string StreamSource { get; set; } = string.Empty;
}

public static class MinidumpParser
{
    private const uint MdmpSignature = 0x504D444D; // 'MDMP'
    private const uint StreamTypeThreadList = 3;
    private const uint StreamTypeModuleList = 4;
    private const uint StreamTypeMemoryList = 5;
    private const uint StreamTypeException = 6;
    private const uint StreamTypeSystemInfo = 7;
    private const uint StreamTypeThreadExList = 8;
    private const uint StreamTypeMemory64List = 9;

    public static bool IsMinidumpFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path!.Trim();
        return (trimmed.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".mdmp", StringComparison.OrdinalIgnoreCase)) &&
               File.Exists(trimmed);
    }

    public static bool TryReadExceptionInfo(string? dumpFilePath, out MinidumpExceptionInfo? info)
    {
        info = null;
        if (!IsMinidumpFile(dumpFilePath))
        {
            return false;
        }

        try
        {
            using var fs = new FileStream(dumpFilePath!.Trim(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            if (fs.Length < 32)
            {
                return false;
            }

            var sig = br.ReadUInt32();
            if (sig != MdmpSignature)
            {
                return false;
            }

            _ = br.ReadUInt32(); // Version
            var numStreams = br.ReadUInt32();
            var dirRva = br.ReadUInt32();

            if (numStreams == 0 || numStreams > 1024 || dirRva + (numStreams * 12L) > fs.Length)
            {
                return false;
            }

            fs.Seek(dirRva, SeekOrigin.Begin);
            uint excRva = 0;
            uint excSize = 0;

            for (uint i = 0; i < numStreams; i++)
            {
                var streamType = br.ReadUInt32();
                var dataSize = br.ReadUInt32();
                var rva = br.ReadUInt32();

                if (streamType == StreamTypeException && dataSize >= 40 && rva > 0 && rva + dataSize <= fs.Length)
                {
                    excRva = rva;
                    excSize = dataSize;
                    break;
                }
            }

            if (excRva == 0 || excSize == 0)
            {
                return false;
            }

            fs.Seek(excRva, SeekOrigin.Begin);
            // MINIDUMP_EXCEPTION_STREAM:
            // ULONG32 ThreadId (4)
            // ULONG32 __alignment (4)
            // MINIDUMP_EXCEPTION ExceptionRecord:
            //   ULONG32 ExceptionCode (4)
            //   ULONG32 ExceptionFlags (4)
            //   ULONG64 ExceptionRecord (8)
            //   ULONG64 ExceptionAddress (8)
            //   ULONG32 NumberParameters (4)
            //   ULONG32 __unusedAlignment (4)
            //   ULONG64 ExceptionInformation[15] (120)
            var threadId = (int)br.ReadUInt32();
            _ = br.ReadUInt32(); // alignment
            var code = br.ReadUInt32();
            _ = br.ReadUInt32(); // flags
            _ = br.ReadUInt64(); // nested ExceptionRecord
            var address = br.ReadUInt64();
            var numParams = br.ReadUInt32();
            _ = br.ReadUInt32(); // unusedAlignment

            ulong p0 = 0;
            ulong p1 = 0;
            if (excSize >= 48 && numParams >= 1)
            {
                p0 = br.ReadUInt64();
            }
            if (excSize >= 56 && numParams >= 2)
            {
                p1 = br.ReadUInt64();
            }

            var codeHex = $"0x{code:X8}";
            var codeName = GetExceptionCodeName(code);
            var addrHex = $"0x{address:X16}";

            string? accessOp = null;
            ulong? faultAddr = null;
            string? faultAddrHex = null;

            // Access Violation (0xC0000005) or In-Page Error (0xC0000006)
            if ((code == 0xC0000005 || code == 0xC0000006) && numParams >= 2)
            {
                accessOp = p0 switch
                {
                    0 => "read",
                    1 => "write",
                    8 => "execute",
                    _ => $"operation({p0})"
                };
                faultAddr = p1;
                faultAddrHex = $"0x{p1:X16}";
            }

            var msgBuilder = new StringBuilder();
            msgBuilder.Append($"Unhandled exception in dump (Thread {threadId}) at {addrHex}: {codeHex} ({codeName})");
            if (accessOp != null && faultAddrHex != null)
            {
                var verb = accessOp switch
                {
                    "read" => "reading",
                    "write" => "writing",
                    "execute" => "executing",
                    _ => "accessing"
                };
                msgBuilder.Append($" - Access violation {verb} location {faultAddrHex}.");
            }
            else
            {
                msgBuilder.Append('.');
            }

            info = new MinidumpExceptionInfo
            {
                ThreadId = threadId,
                ExceptionCode = code,
                ExceptionCodeHex = codeHex,
                ExceptionName = codeName,
                ExceptionAddress = address,
                ExceptionAddressHex = addrHex,
                AccessOperation = accessOp,
                FaultingAddress = faultAddr,
                FaultingAddressHex = faultAddrHex,
                FormattedMessage = msgBuilder.ToString()
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadMemory(string? dumpFilePath, ulong virtualAddress, int byteCount, out byte[] buffer)
    {
        buffer = Array.Empty<byte>();
        if (!IsMinidumpFile(dumpFilePath) || byteCount <= 0)
        {
            return false;
        }

        try
        {
            using var fs = new FileStream(dumpFilePath!.Trim(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            if (fs.Length < 32)
            {
                return false;
            }

            if (br.ReadUInt32() != MdmpSignature)
            {
                return false;
            }

            _ = br.ReadUInt32(); // Version
            var numStreams = br.ReadUInt32();
            var dirRva = br.ReadUInt32();

            if (numStreams == 0 || numStreams > 1024 || dirRva + (numStreams * 12L) > fs.Length)
            {
                return false;
            }

            fs.Seek(dirRva, SeekOrigin.Begin);
            uint mem64Rva = 0, mem64Size = 0;
            uint mem32Rva = 0, mem32Size = 0;

            for (uint i = 0; i < numStreams; i++)
            {
                var streamType = br.ReadUInt32();
                var dataSize = br.ReadUInt32();
                var rva = br.ReadUInt32();

                if (streamType == StreamTypeMemory64List && dataSize >= 16)
                {
                    mem64Rva = rva;
                    mem64Size = dataSize;
                }
                else if (streamType == StreamTypeMemoryList && dataSize >= 4)
                {
                    mem32Rva = rva;
                    mem32Size = dataSize;
                }
            }

            var ranges = new List<(ulong StartVa, ulong Size, long FileOffset)>();

            if (mem64Rva > 0 && mem64Rva + 16L <= fs.Length)
            {
                fs.Seek(mem64Rva, SeekOrigin.Begin);
                var numRanges = br.ReadUInt64();
                var baseRva = br.ReadUInt64();
                var currentFileOffset = (long)baseRva;

                var maxPossible = (ulong)((mem64Size - 16) / 16);
                var count = Math.Min(numRanges, maxPossible);

                for (ulong i = 0; i < count; i++)
                {
                    var startVa = br.ReadUInt64();
                    var dataSize = br.ReadUInt64();
                    ranges.Add((startVa, dataSize, currentFileOffset));
                    currentFileOffset += (long)dataSize;
                }
            }
            else if (mem32Rva > 0 && mem32Rva + 4L <= fs.Length)
            {
                fs.Seek(mem32Rva, SeekOrigin.Begin);
                var numRanges = br.ReadUInt32();
                var maxPossible = (mem32Size - 4) / 16;
                var count = Math.Min(numRanges, maxPossible);

                for (uint i = 0; i < count; i++)
                {
                    var startVa = br.ReadUInt64();
                    var dataSize = br.ReadUInt32();
                    var rva = br.ReadUInt32();
                    ranges.Add((startVa, dataSize, rva));
                }
            }

            if (ranges.Count == 0)
            {
                return false;
            }

            var result = new List<byte>(Math.Min(byteCount, 4096));
            var currentTargetVa = virtualAddress;
            var remaining = byteCount;

            while (remaining > 0)
            {
                var foundSegment = false;
                for (int i = 0; i < ranges.Count; i++)
                {
                    var r = ranges[i];
                    if (currentTargetVa >= r.StartVa && currentTargetVa < r.StartVa + r.Size)
                    {
                        var offsetInSegment = currentTargetVa - r.StartVa;
                        var availableInSegment = r.Size - offsetInSegment;
                        var toRead = (int)Math.Min((ulong)remaining, availableInSegment);
                        var filePos = r.FileOffset + (long)offsetInSegment;

                        if (toRead > 0 && filePos >= 0 && filePos + toRead <= fs.Length)
                        {
                            fs.Seek(filePos, SeekOrigin.Begin);
                            var chunk = br.ReadBytes(toRead);
                            if (chunk.Length > 0)
                            {
                                result.AddRange(chunk);
                                currentTargetVa += (ulong)chunk.Length;
                                remaining -= chunk.Length;
                                foundSegment = true;
                            }
                        }
                        break;
                    }
                }

                if (!foundSegment)
                {
                    break;
                }
            }

            if (result.Count == 0)
            {
                return false;
            }

            buffer = result.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? ExtractUtf16Preview(byte[]? buffer, int maxChars = 128)
    {
        if (buffer == null || buffer.Length < 4)
        {
            return null;
        }

        // Scan for contiguous printable UTF-16LE sequences of length >= 3
        var bestSegment = string.Empty;
        var current = new StringBuilder();

        for (int i = 0; i + 1 < buffer.Length; i += 2)
        {
            char ch = (char)(buffer[i] | (buffer[i + 1] << 8));
            if (IsPrintableUtf16Char(ch))
            {
                current.Append(ch);
                if (current.Length >= maxChars)
                {
                    break;
                }
            }
            else
            {
                if (current.Length > bestSegment.Length)
                {
                    bestSegment = current.ToString();
                    // If the very first string at offset 0 is already >= 3 chars, prefer it immediately
                    if (i - (current.Length * 2) == 0 && bestSegment.Length >= 3)
                    {
                        return bestSegment;
                    }
                }
                current.Clear();
            }
        }

        if (current.Length > bestSegment.Length)
        {
            bestSegment = current.ToString();
        }

        return bestSegment.Length >= 3 ? bestSegment : null;
    }

    private static bool IsPrintableUtf16Char(char ch)
    {
        if (ch >= 0x20 && ch <= 0x7E)
        {
            return true;
        }

        // Common CJK Unified Ideographs
        if (ch >= 0x4E00 && ch <= 0x9FFF)
        {
            return true;
        }

        return false;
    }

    public static string GetExceptionCodeName(uint code) => code switch
    {
        0xC0000005 => "EXCEPTION_ACCESS_VIOLATION",
        0xC0000006 => "EXCEPTION_IN_PAGE_ERROR",
        0xC000001D => "EXCEPTION_ILLEGAL_INSTRUCTION",
        0xC0000094 => "EXCEPTION_INT_DIVIDE_BY_ZERO",
        0xC00000FD => "EXCEPTION_STACK_OVERFLOW",
        0xC0000374 => "STATUS_HEAP_CORRUPTION",
        0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN",
        0x80000003 => "EXCEPTION_BREAKPOINT",
        0x80000004 => "EXCEPTION_SINGLE_STEP",
        0xE06D7363 => "C++ EH Exception",
        0xE0434352 => "CLR Managed Exception",
        _ => "NATIVE_EXCEPTION"
    };

    public static bool TryReadPeImageBase(string? peFilePath, out ulong imageBase)
    {
        imageBase = 0x180000000UL;
        if (string.IsNullOrWhiteSpace(peFilePath) || !File.Exists(peFilePath))
        {
            return false;
        }

        try
        {
            using var fs = new FileStream(peFilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            if (fs.Length < 0x100)
            {
                return false;
            }

            if (br.ReadUInt16() != 0x5A4D) // 'MZ'
            {
                return false;
            }

            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset + 0x58 > fs.Length)
            {
                return false;
            }

            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) // 'PE\0\0'
            {
                return false;
            }

            // Skip COFF File Header (20 bytes)
            fs.Seek(20, SeekOrigin.Current);
            var magic = br.ReadUInt16();
            if (magic == 0x20B) // PE32+ (64-bit)
            {
                // ImageBase is at offset 24 from start of OptionalHeader (22 bytes after Magic)
                fs.Seek(22, SeekOrigin.Current);
                imageBase = br.ReadUInt64();
                return true;
            }
            if (magic == 0x10B) // PE32 (32-bit)
            {
                // ImageBase is at offset 28 from start of OptionalHeader (26 bytes after Magic)
                fs.Seek(26, SeekOrigin.Current);
                imageBase = br.ReadUInt32();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static readonly Regex s_disasmLineRegex = new(
        @"^\s*(?<addr>[0-9A-Fa-f]{8,16}):\s+(?<bytes>(?:[0-9A-Fa-f]{2}\s+)+)\s*(?<inst>.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex s_hexTargetRegex = new(
        @"\b(?<target>[0-9A-Fa-f]{12,16})h?\b",
        RegexOptions.Compiled);

    public static List<DisassemblyInstruction> ParseAndRebaseDumpbinOutput(
        string dumpbinOutput,
        ulong targetRuntimeAddress,
        ulong moduleLoadAddress,
        ulong peImageBase,
        int instructionCount,
        int instructionsBefore)
    {
        if (string.IsNullOrWhiteSpace(dumpbinOutput))
        {
            return new List<DisassemblyInstruction>();
        }

        var allParsed = new List<(ulong RuntimeAddr, string Bytes, string Instruction, string? Symbol)>();
        string? currentSymbol = null;

        var lines = dumpbinOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var m = s_disasmLineRegex.Match(rawLine);
            if (m.Success)
            {
                if (ulong.TryParse(m.Groups["addr"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var staticVa))
                {
                    var rva = staticVa >= peImageBase ? staticVa - peImageBase : staticVa;
                    var runtimeVa = moduleLoadAddress > 0 ? moduleLoadAddress + rva : staticVa;
                    var bytesStr = Regex.Replace(m.Groups["bytes"].Value.Trim(), @"\s+", " ");
                    var instText = m.Groups["inst"].Value.Trim();
                    if (string.IsNullOrEmpty(instText) || !char.IsLetter(instText[0]))
                    {
                        continue;
                    }

                    // Rebase static target addresses inside call/jmp operands if moduleLoadAddress > 0
                    if (moduleLoadAddress > 0 && peImageBase > 0 && moduleLoadAddress != peImageBase)
                    {
                        instText = s_hexTargetRegex.Replace(instText, match =>
                        {
                            var hexStr = match.Groups["target"].Value;
                            if (ulong.TryParse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var opVa))
                            {
                                if (opVa >= peImageBase && opVa < peImageBase + 0x20000000UL)
                                {
                                    var rebased = moduleLoadAddress + (opVa - peImageBase);
                                    return $"0x{rebased:X16}";
                                }
                            }
                            return match.Value;
                        });
                    }

                    allParsed.Add((runtimeVa, bytesStr, instText, currentSymbol));
                }
            }
            else
            {
                var trimmed = rawLine.Trim();
                if (trimmed.EndsWith(":", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("Dump of", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("File Type", StringComparison.OrdinalIgnoreCase))
                {
                    currentSymbol = trimmed.Substring(0, trimmed.Length - 1).Trim();
                }
            }
        }

        if (allParsed.Count == 0)
        {
            return new List<DisassemblyInstruction>();
        }

        // Find index closest to targetRuntimeAddress
        int targetIdx = 0;
        ulong minDiff = ulong.MaxValue;
        for (int i = 0; i < allParsed.Count; i++)
        {
            var addr = allParsed[i].RuntimeAddr;
            var diff = addr >= targetRuntimeAddress ? addr - targetRuntimeAddress : targetRuntimeAddress - addr;
            if (diff < minDiff)
            {
                minDiff = diff;
                targetIdx = i;
                if (diff == 0)
                {
                    break;
                }
            }
        }

        int startIdx = Math.Max(0, targetIdx - Math.Max(0, instructionsBefore));
        int endIdx = Math.Min(allParsed.Count - 1, startIdx + Math.Max(1, instructionCount) - 1);

        var result = new List<DisassemblyInstruction>(endIdx - startIdx + 1);
        for (int i = startIdx; i <= endIdx; i++)
        {
            var item = allParsed[i];
            result.Add(new DisassemblyInstruction
            {
                Address = $"0x{item.RuntimeAddr:X16}",
                Bytes = item.Bytes,
                Instruction = item.Instruction,
                Symbol = item.Symbol,
                IsCurrentInstruction = i == targetIdx
            });
        }

        return result;
    }

    public static List<DisassemblyInstruction> DisassembleMemoryBytes(
        byte[]? codeBytes,
        ulong baseAddress,
        int bitness = 64,
        ulong? currentInstructionAddress = null,
        int maxInstructions = 30)
    {
        var result = new List<DisassemblyInstruction>();
        if (codeBytes == null || codeBytes.Length == 0)
        {
            return result;
        }

        try
        {
            var codeReader = new ByteArrayCodeReader(codeBytes);
            var decoder = Iced.Intel.Decoder.Create(bitness, codeReader, baseAddress);
            var formatter = new IntelFormatter();
            var output = new StringOutput();

            while (codeReader.CanReadByte && result.Count < maxInstructions)
            {
                decoder.Decode(out var instruction);
                output.Reset();
                formatter.Format(instruction, output);

                var ip = instruction.IP;
                var len = instruction.Length;
                var offsetInBytes = (int)(ip - baseAddress);

                string bytesHex = string.Empty;
                if (offsetInBytes >= 0 && offsetInBytes + len <= codeBytes.Length)
                {
                    var hexParts = new string[len];
                    for (int i = 0; i < len; i++)
                    {
                        hexParts[i] = codeBytes[offsetInBytes + i].ToString("X2");
                    }
                    bytesHex = string.Join(" ", hexParts);
                }

                result.Add(new DisassemblyInstruction
                {
                    Address = bitness == 64 ? $"0x{ip:X16}" : $"0x{ip:X8}",
                    Bytes = bytesHex,
                    Instruction = output.ToString(),
                    IsCurrentInstruction = currentInstructionAddress.HasValue && ip == currentInstructionAddress.Value
                });
            }
        }
        catch
        {
            // Ignore disassembly decoding errors gracefully
        }

        return result;
    }

    public static bool TryReadThreadContext(string? dumpFilePath, int? targetThreadId, out CpuContextInfo? context)
    {
        context = null;
        if (!IsMinidumpFile(dumpFilePath))
        {
            return false;
        }

        try
        {
            using var fs = new FileStream(dumpFilePath!.Trim(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            if (fs.Length < 32 || br.ReadUInt32() != MdmpSignature)
            {
                return false;
            }

            _ = br.ReadUInt32(); // Version
            var numStreams = br.ReadUInt32();
            var dirRva = br.ReadUInt32();

            if (numStreams == 0 || numStreams > 1024 || dirRva + (numStreams * 12L) > fs.Length)
            {
                return false;
            }

            fs.Seek(dirRva, SeekOrigin.Begin);
            uint excRva = 0, excSize = 0;
            uint threadListRva = 0, threadListSize = 0;

            for (uint i = 0; i < numStreams; i++)
            {
                var streamType = br.ReadUInt32();
                var dataSize = br.ReadUInt32();
                var rva = br.ReadUInt32();

                if (streamType == StreamTypeException)
                {
                    excRva = rva;
                    excSize = dataSize;
                }
                else if (streamType == StreamTypeThreadList)
                {
                    threadListRva = rva;
                    threadListSize = dataSize;
                }
            }

            uint contextRva = 0;
            uint contextSize = 0;
            int matchedThreadId = targetThreadId ?? 0;

            // 1. If no specific thread requested or target matches exception thread, prefer exception context
            if (excRva > 0 && excSize >= 168)
            {
                fs.Seek(excRva, SeekOrigin.Begin);
                var excThreadId = (int)br.ReadUInt32();
                if (targetThreadId == null || targetThreadId.Value == excThreadId)
                {
                    matchedThreadId = excThreadId;
                    fs.Seek(excRva + 8 + 152, SeekOrigin.Begin); // MINIDUMP_EXCEPTION is 152 bytes
                    contextSize = br.ReadUInt32();
                    contextRva = br.ReadUInt32();
                }
            }

            // 2. If not found yet and ThreadList is available, scan threads
            if ((contextRva == 0 || contextSize == 0) && threadListRva > 0 && threadListSize >= 4)
            {
                fs.Seek(threadListRva, SeekOrigin.Begin);
                var numThreads = br.ReadUInt32();
                for (uint i = 0; i < numThreads && fs.Position + 48 <= fs.Length; i++)
                {
                    var tId = (int)br.ReadUInt32();
                    fs.Seek(4 + 4 + 4 + 8 + 16, SeekOrigin.Current); // SuspendCount(4)+PriorityClass(4)+Priority(4)+Teb(8)+Stack(16)
                    var cSize = br.ReadUInt32();
                    var cRva = br.ReadUInt32();

                    if (targetThreadId == null || tId == targetThreadId.Value)
                    {
                        matchedThreadId = tId;
                        contextSize = cSize;
                        contextRva = cRva;
                        break;
                    }
                }
            }

            if (contextRva == 0 || contextSize == 0 || contextRva + contextSize > fs.Length)
            {
                return false;
            }

            fs.Seek(contextRva, SeekOrigin.Begin);
            if (contextSize >= 0x4D0)
            {
                // AMD64 (x64) Context
                fs.Seek(contextRva + 0x44, SeekOrigin.Begin);
                var eflags = br.ReadUInt32();
                fs.Seek(contextRva + 0x78, SeekOrigin.Begin);
                var rax = br.ReadUInt64();
                var rcx = br.ReadUInt64();
                var rdx = br.ReadUInt64();
                var rbx = br.ReadUInt64();
                var rsp = br.ReadUInt64();
                var rbp = br.ReadUInt64();
                var rsi = br.ReadUInt64();
                var rdi = br.ReadUInt64();
                var r8  = br.ReadUInt64();
                var r9  = br.ReadUInt64();
                var r10 = br.ReadUInt64();
                var r11 = br.ReadUInt64();
                var r12 = br.ReadUInt64();
                var r13 = br.ReadUInt64();
                var r14 = br.ReadUInt64();
                var r15 = br.ReadUInt64();
                var rip = br.ReadUInt64();

                var regDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RAX"] = $"0x{rax:X16}",
                    ["RBX"] = $"0x{rbx:X16}",
                    ["RCX"] = $"0x{rcx:X16}",
                    ["RDX"] = $"0x{rdx:X16}",
                    ["RSI"] = $"0x{rsi:X16}",
                    ["RDI"] = $"0x{rdi:X16}",
                    ["RBP"] = $"0x{rbp:X16}",
                    ["RSP"] = $"0x{rsp:X16}",
                    ["R8"]  = $"0x{r8:X16}",
                    ["R9"]  = $"0x{r9:X16}",
                    ["R10"] = $"0x{r10:X16}",
                    ["R11"] = $"0x{r11:X16}",
                    ["R12"] = $"0x{r12:X16}",
                    ["R13"] = $"0x{r13:X16}",
                    ["R14"] = $"0x{r14:X16}",
                    ["R15"] = $"0x{r15:X16}",
                    ["RIP"] = $"0x{rip:X16}",
                    ["RFLAGS"] = $"0x{eflags:X8}"
                };

                var detailed = new List<RegisterValueInfo>
                {
                    new() { Name = "RAX", ValueHex = $"0x{rax:X16}", Value64 = rax },
                    new() { Name = "RBX", ValueHex = $"0x{rbx:X16}", Value64 = rbx },
                    new() { Name = "RCX", ValueHex = $"0x{rcx:X16}", Value64 = rcx },
                    new() { Name = "RDX", ValueHex = $"0x{rdx:X16}", Value64 = rdx },
                    new() { Name = "RSI", ValueHex = $"0x{rsi:X16}", Value64 = rsi },
                    new() { Name = "RDI", ValueHex = $"0x{rdi:X16}", Value64 = rdi },
                    new() { Name = "RBP", ValueHex = $"0x{rbp:X16}", Value64 = rbp },
                    new() { Name = "RSP", ValueHex = $"0x{rsp:X16}", Value64 = rsp },
                    new() { Name = "R8",  ValueHex = $"0x{r8:X16}",  Value64 = r8 },
                    new() { Name = "R9",  ValueHex = $"0x{r9:X16}",  Value64 = r9 },
                    new() { Name = "R10", ValueHex = $"0x{r10:X16}", Value64 = r10 },
                    new() { Name = "R11", ValueHex = $"0x{r11:X16}", Value64 = r11 },
                    new() { Name = "R12", ValueHex = $"0x{r12:X16}", Value64 = r12 },
                    new() { Name = "R13", ValueHex = $"0x{r13:X16}", Value64 = r13 },
                    new() { Name = "R14", ValueHex = $"0x{r14:X16}", Value64 = r14 },
                    new() { Name = "R15", ValueHex = $"0x{r15:X16}", Value64 = r15 },
                    new() { Name = "RIP", ValueHex = $"0x{rip:X16}", Value64 = rip },
                    new() { Name = "RFLAGS", ValueHex = $"0x{eflags:X8}", Value64 = eflags }
                };

                var flagBits = new Dictionary<string, bool>
                {
                    ["CF"] = (eflags & 1) != 0,
                    ["PF"] = (eflags & 4) != 0,
                    ["AF"] = (eflags & 16) != 0,
                    ["ZF"] = (eflags & 64) != 0,
                    ["SF"] = (eflags & 128) != 0,
                    ["OF"] = (eflags & 2048) != 0
                };

                context = new CpuContextInfo
                {
                    ThreadId = matchedThreadId,
                    Architecture = "x64",
                    Registers = regDict,
                    DetailedRegisters = detailed,
                    InstructionPointer = $"0x{rip:X16}",
                    InstructionPointer64 = rip,
                    StackPointer = $"0x{rsp:X16}",
                    StackPointer64 = rsp,
                    FramePointer = $"0x{rbp:X16}",
                    FramePointer64 = rbp,
                    FlagsHex = $"0x{eflags:X8}",
                    FlagBits = flagBits
                };
                return true;
            }
            else if (contextSize >= 716)
            {
                // x86 Context
                fs.Seek(contextRva + 0x9C, SeekOrigin.Begin);
                var edi = br.ReadUInt32();
                var esi = br.ReadUInt32();
                var ebx = br.ReadUInt32();
                var edx = br.ReadUInt32();
                var ecx = br.ReadUInt32();
                var eax = br.ReadUInt32();
                var ebp = br.ReadUInt32();
                var eip = br.ReadUInt32();
                _ = br.ReadUInt32(); // cs
                var eflags = br.ReadUInt32();
                var esp = br.ReadUInt32();

                var regDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EAX"] = $"0x{eax:X8}",
                    ["EBX"] = $"0x{ebx:X8}",
                    ["ECX"] = $"0x{ecx:X8}",
                    ["EDX"] = $"0x{edx:X8}",
                    ["ESI"] = $"0x{esi:X8}",
                    ["EDI"] = $"0x{edi:X8}",
                    ["EBP"] = $"0x{ebp:X8}",
                    ["ESP"] = $"0x{esp:X8}",
                    ["EIP"] = $"0x{eip:X8}",
                    ["EFLAGS"] = $"0x{eflags:X8}"
                };

                var detailed = new List<RegisterValueInfo>
                {
                    new() { Name = "EAX", ValueHex = $"0x{eax:X8}", Value64 = eax },
                    new() { Name = "EBX", ValueHex = $"0x{ebx:X8}", Value64 = ebx },
                    new() { Name = "ECX", ValueHex = $"0x{ecx:X8}", Value64 = ecx },
                    new() { Name = "EDX", ValueHex = $"0x{edx:X8}", Value64 = edx },
                    new() { Name = "ESI", ValueHex = $"0x{esi:X8}", Value64 = esi },
                    new() { Name = "EDI", ValueHex = $"0x{edi:X8}", Value64 = edi },
                    new() { Name = "EBP", ValueHex = $"0x{ebp:X8}", Value64 = ebp },
                    new() { Name = "ESP", ValueHex = $"0x{esp:X8}", Value64 = esp },
                    new() { Name = "EIP", ValueHex = $"0x{eip:X8}", Value64 = eip },
                    new() { Name = "EFLAGS", ValueHex = $"0x{eflags:X8}", Value64 = eflags }
                };

                var flagBits = new Dictionary<string, bool>
                {
                    ["CF"] = (eflags & 1) != 0,
                    ["PF"] = (eflags & 4) != 0,
                    ["AF"] = (eflags & 16) != 0,
                    ["ZF"] = (eflags & 64) != 0,
                    ["SF"] = (eflags & 128) != 0,
                    ["OF"] = (eflags & 2048) != 0
                };

                context = new CpuContextInfo
                {
                    ThreadId = matchedThreadId,
                    Architecture = "x86",
                    Registers = regDict,
                    DetailedRegisters = detailed,
                    InstructionPointer = $"0x{eip:X8}",
                    InstructionPointer64 = eip,
                    StackPointer = $"0x{esp:X8}",
                    StackPointer64 = esp,
                    FramePointer = $"0x{ebp:X8}",
                    FramePointer64 = ebp,
                    FlagsHex = $"0x{eflags:X8}",
                    FlagBits = flagBits
                };
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadCrashSummary(string? dumpFilePath, out MinidumpCrashSummary? summary)
    {
        summary = null;
        if (!TryReadExceptionInfo(dumpFilePath, out var excInfo) || excInfo == null)
        {
            return false;
        }

        var regDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? disasmText = null;

        if (TryReadThreadContext(dumpFilePath, excInfo.ThreadId, out var ctxInfo) && ctxInfo != null)
        {
            regDict = ctxInfo.Registers;
            var bitness = string.Equals(ctxInfo.Architecture, "x86", StringComparison.OrdinalIgnoreCase) ? 32 : 64;

            // Try reading instructions at RIP
            if (TryReadMemory(dumpFilePath, excInfo.ExceptionAddress, 32, out var codeBytes) && codeBytes.Length > 0)
            {
                var instructions = DisassembleMemoryBytes(codeBytes, excInfo.ExceptionAddress, bitness, excInfo.ExceptionAddress, 8);
                if (instructions.Count > 0)
                {
                    disasmText = FormatDisassemblyText(instructions);
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Minidump Crash Summary ===");
        sb.AppendLine($"Thread ID        : {excInfo.ThreadId}");
        sb.AppendLine($"Exception Code   : {excInfo.ExceptionCodeHex} ({excInfo.ExceptionName})");
        sb.AppendLine($"Exception Address: {excInfo.ExceptionAddressHex}");
        if (excInfo.AccessOperation != null && excInfo.FaultingAddressHex != null)
        {
            sb.AppendLine($"Access Violation : {excInfo.AccessOperation} at {excInfo.FaultingAddressHex}");
        }

        if (!string.IsNullOrWhiteSpace(disasmText))
        {
            sb.AppendLine();
            sb.AppendLine("Faulting Instruction Disassembly:");
            sb.AppendLine(disasmText);
        }

        if (regDict.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Registers Snapshot:");
            var regNames = regDict.Keys.ToList();
            for (int i = 0; i < regNames.Count; i += 2)
            {
                var r1 = regNames[i];
                var v1 = regDict[r1];
                if (i + 1 < regNames.Count)
                {
                    var r2 = regNames[i + 1];
                    var v2 = regDict[r2];
                    sb.AppendLine($"  {r1,-6}: {v1,-18}  {r2,-6}: {v2,-18}");
                }
                else
                {
                    sb.AppendLine($"  {r1,-6}: {v1,-18}");
                }
            }
        }

        summary = new MinidumpCrashSummary
        {
            ThreadId = excInfo.ThreadId,
            ExceptionCodeHex = excInfo.ExceptionCodeHex,
            ExceptionName = excInfo.ExceptionName,
            ExceptionAddressHex = excInfo.ExceptionAddressHex,
            ExceptionAddress = excInfo.ExceptionAddress,
            AccessOperation = excInfo.AccessOperation,
            FaultingAddressHex = excInfo.FaultingAddressHex,
            FaultingAddress = excInfo.FaultingAddress,
            Registers = regDict,
            FaultingInstructionDisassembly = disasmText,
            FormattedSummary = sb.ToString()
        };
        return true;
    }

    public static bool TryQueryMemoryInDump(string? dumpFilePath, ulong virtualAddress, out MinidumpMemoryQueryInfo? info)
    {
        info = null;
        if (!IsMinidumpFile(dumpFilePath))
        {
            return false;
        }

        try
        {
            using var fs = new FileStream(dumpFilePath!.Trim(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            if (fs.Length < 32 || br.ReadUInt32() != MdmpSignature)
            {
                return false;
            }

            _ = br.ReadUInt32();
            var numStreams = br.ReadUInt32();
            var dirRva = br.ReadUInt32();

            fs.Seek(dirRva, SeekOrigin.Begin);
            uint mem64Rva = 0, mem64Size = 0;
            uint mem32Rva = 0, mem32Size = 0;

            for (uint i = 0; i < numStreams; i++)
            {
                var streamType = br.ReadUInt32();
                var dataSize = br.ReadUInt32();
                var rva = br.ReadUInt32();

                if (streamType == StreamTypeMemory64List && dataSize >= 16)
                {
                    mem64Rva = rva;
                    mem64Size = dataSize;
                }
                else if (streamType == StreamTypeMemoryList && dataSize >= 4)
                {
                    mem32Rva = rva;
                    mem32Size = dataSize;
                }
            }

            if (mem64Rva > 0 && mem64Rva + 16L <= fs.Length)
            {
                fs.Seek(mem64Rva, SeekOrigin.Begin);
                var numRanges = br.ReadUInt64();
                _ = br.ReadUInt64(); // baseRva
                var maxPossible = (ulong)((mem64Size - 16) / 16);
                var count = Math.Min(numRanges, maxPossible);

                for (ulong i = 0; i < count; i++)
                {
                    var startVa = br.ReadUInt64();
                    var dataSize = br.ReadUInt64();
                    if (virtualAddress >= startVa && virtualAddress < startVa + dataSize)
                    {
                        info = new MinidumpMemoryQueryInfo
                        {
                            Address = virtualAddress,
                            AddressHex = $"0x{virtualAddress:X16}",
                            IsPresentInDump = true,
                            BaseAddress = startVa,
                            BaseAddressHex = $"0x{startVa:X16}",
                            RegionSize = dataSize,
                            RegionSizeHex = $"0x{dataSize:X}",
                            StreamSource = "Memory64ListStream"
                        };
                        return true;
                    }
                }
            }
            else if (mem32Rva > 0 && mem32Rva + 4L <= fs.Length)
            {
                fs.Seek(mem32Rva, SeekOrigin.Begin);
                var numRanges = br.ReadUInt32();
                var maxPossible = (mem32Size - 4) / 16;
                var count = Math.Min(numRanges, maxPossible);

                for (uint i = 0; i < count; i++)
                {
                    var startVa = br.ReadUInt64();
                    var dataSize = br.ReadUInt32();
                    _ = br.ReadUInt32(); // rva
                    if (virtualAddress >= startVa && virtualAddress < startVa + dataSize)
                    {
                        info = new MinidumpMemoryQueryInfo
                        {
                            Address = virtualAddress,
                            AddressHex = $"0x{virtualAddress:X16}",
                            IsPresentInDump = true,
                            BaseAddress = startVa,
                            BaseAddressHex = $"0x{startVa:X16}",
                            RegionSize = dataSize,
                            RegionSizeHex = $"0x{dataSize:X}",
                            StreamSource = "MemoryListStream"
                        };
                        return true;
                    }
                }
            }

            info = new MinidumpMemoryQueryInfo
            {
                Address = virtualAddress,
                AddressHex = $"0x{virtualAddress:X16}",
                IsPresentInDump = false,
                BaseAddress = 0,
                BaseAddressHex = "0x0",
                RegionSize = 0,
                RegionSizeHex = "0x0",
                StreamSource = "NotPresentInDump"
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string FormatDisassemblyText(IReadOnlyList<DisassemblyInstruction> instructions)
    {
        if (instructions == null || instructions.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < instructions.Count; i++)
        {
            var inst = instructions[i];
            var prefix = inst.IsCurrentInstruction ? ">>> " : "    ";
            var bytesCol = string.IsNullOrWhiteSpace(inst.Bytes)
                ? string.Empty
                : $"{inst.Bytes!.PadRight(24)} ";
            sb.Append(prefix)
              .Append(inst.Address)
              .Append(": ")
              .Append(bytesCol)
              .Append(inst.Instruction);

            if (!string.IsNullOrWhiteSpace(inst.SourceFile) && inst.SourceLine.HasValue)
            {
                sb.Append($"  ; {Path.GetFileName(inst.SourceFile)}:{inst.SourceLine.Value}");
            }

            if (i + 1 < instructions.Count)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
