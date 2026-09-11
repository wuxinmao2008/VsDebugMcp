using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VsDebugMcp_Vsix.Diagnostics;

namespace VsDebugMcp_Vsix;

internal sealed class SharedHostProcessManager
{
    private const string LogSource = "VsDebugMcp";
    private readonly HostControlClient _client = new();
    private readonly IVsDiagnosticSink? _diagnostics;

    public SharedHostProcessManager(IVsDiagnosticSink? diagnostics = null)
    {
        _diagnostics = diagnostics;
    }

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        try
        {
            _diagnostics?.LogInfo("正在探测 Host 控制管道状态...");
            await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            _diagnostics?.LogInfo("检测到已运行的 Host 实例，状态正常。");
            return true;
        }
        catch (HostControlException exception)
        {
            ActivityLog.LogError(LogSource, exception.Code);
            _diagnostics?.LogError("Host 控制管道响应异常: " + exception.Code, exception.Code);
            return false;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            _diagnostics?.LogInfo("未检测到活跃的 Host 进程，准备拉起内置 Host。");
        }

        var hostPath = Path.Combine(
            Path.GetDirectoryName(typeof(VsDebugMcp_VsixPackage).Assembly.Location) ?? string.Empty,
            "Host",
            "VsDebugMcp.Host.exe");
        if (!File.Exists(hostPath))
        {
            ActivityLog.LogError(LogSource, "host_executable_unavailable");
            _diagnostics?.LogError($"未在扩展目录中找到 Host 可执行文件: {hostPath}", "host_executable_unavailable");
            _diagnostics?.ShowErrorBanner("未找到 Host 可执行文件，请确认 VSIX 是否完整构建部署", "host_executable_unavailable");
            return false;
        }

        Process? hostProcess = null;
        try
        {
            _diagnostics?.LogInfo($"正在准备启动 Host 进程: {hostPath}");
            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                WorkingDirectory = Path.GetDirectoryName(hostPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var resolvedDotNetRoot = TryResolveDotNetRoot();
            if (string.IsNullOrEmpty(resolvedDotNetRoot))
            {
                ActivityLog.LogError(LogSource, "net8_runtime_missing");
                _diagnostics?.LogError("未检测到可用的 64 位 .NET 8 运行时（VS 2017/2019 环境需系统安装 64 位 .NET 8）。请安装 .NET 8 Desktop Runtime: https://aka.ms/dotnet/8.0/runtime", "net8_runtime_missing");
                _diagnostics?.ShowErrorBanner("缺少 64 位 .NET 8 运行时，请安装 .NET 8 Desktop Runtime: https://aka.ms/dotnet/8.0/runtime", "net8_runtime_missing");
                return false;
            }

            _diagnostics?.LogInfo($"已为 Host 配置 .NET 运行时目录 (DOTNET_ROOT): {resolvedDotNetRoot}");
            startInfo.EnvironmentVariables["DOTNET_ROOT"] = resolvedDotNetRoot;
            startInfo.EnvironmentVariables.Remove("DOTNET_ROOT(x86)");

            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.EnvironmentVariables["PATH"] = resolvedDotNetRoot + Path.PathSeparator + currentPath;

            hostProcess = Process.Start(startInfo);
            if (hostProcess is not null)
            {
                JobObjectHelper.TryAssignProcess(hostProcess);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ActivityLog.LogError(LogSource, "host_start_failed");
            _diagnostics?.LogError("启动 Host 进程失败: " + exception.Message, "host_start_failed");
            _diagnostics?.ShowErrorBanner("启动 Host 进程失败", "host_start_failed");
            return false;
        }

        _diagnostics?.LogInfo("Host 进程已拉起，正在等待控制管道就绪...");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (hostProcess != null && hostProcess.HasExited)
            {
                ActivityLog.LogError(LogSource, "host_process_exited");
                _diagnostics?.LogError($"Host 进程异常退出，退出码: {hostProcess.ExitCode} (0x{hostProcess.ExitCode:X})。请确认已安装 64 位 .NET 8 桌面运行时。", "host_process_exited");
                _diagnostics?.ShowErrorBanner("Host 进程异常退出，请确认安装了 64 位 .NET 8 Runtime", "host_process_exited");
                return false;
            }

            try
            {
                await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                _diagnostics?.LogInfo($"Host 控制管道已就绪（耗时约 {(attempt + 1) * 250}ms）。");
                return true;
            }
            catch (HostControlException exception)
            {
                ActivityLog.LogError(LogSource, exception.Code);
                _diagnostics?.LogError("Host 控制管道响应异常: " + exception.Code, exception.Code);
                return false;
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        ActivityLog.LogError(LogSource, "host_start_timeout");
        _diagnostics?.LogError("等待 Host 上线超时（15秒）。可能原因：端口 43260 被占用或 Host 异常退出。", "host_start_timeout");
        _diagnostics?.ShowErrorBanner("Host 上线超时，请检查端口 43260 是否被占用", "host_start_timeout");
        return false;
    }

    private string? TryResolveDotNetRoot()
    {
        try
        {
            var devenvPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(devenvPath))
            {
                var ideDir = Path.GetDirectoryName(devenvPath);
                var common7Dir = ideDir != null ? Path.GetDirectoryName(ideDir) : null;
                var vsInstallDir = common7Dir != null ? Path.GetDirectoryName(common7Dir) : null;
                if (vsInstallDir != null)
                {
                    var vsDotnetRoot = Path.Combine(vsInstallDir, "dotnet", "net8.0", "runtime");
                    if (File.Exists(Path.Combine(vsDotnetRoot, "dotnet.exe")))
                    {
                        return vsDotnetRoot;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _diagnostics?.LogInfo($"探测 VS 内置 .NET 运行时异常: {exception.Message}");
        }

        // 2. VsDebugMcp.Host is published as win-x64. On 64-bit Windows, a 32-bit IDE process (VS 2017/2019)
        // returns "C:\Program Files (x86)" for SpecialFolder.ProgramFiles.
        // We MUST prioritize 64-bit Program Files (ProgramW6432) so the 64-bit Host loads the 64-bit .NET 8 runtime!
        var programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        if (!string.IsNullOrEmpty(programW6432))
        {
            var x64Path = Path.Combine(programW6432, "dotnet");
            if (File.Exists(Path.Combine(x64Path, "dotnet.exe")) && HasNet8Runtime(x64Path))
            {
                return x64Path;
            }
        }

        // 3. Fallback to standard ProgramFiles (which is 64-bit on 64-bit OS when running in 64-bit process)
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        if (File.Exists(Path.Combine(defaultPath, "dotnet.exe")) && HasNet8Runtime(defaultPath))
        {
            return defaultPath;
        }

        // 4. Fallback to DOTNET_ROOT environment variable
        var envDotNetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(envDotNetRoot) && File.Exists(Path.Combine(envDotNetRoot, "dotnet.exe")) && HasNet8Runtime(envDotNetRoot))
        {
            return envDotNetRoot;
        }

        return null;
    }

    private static bool HasNet8Runtime(string dotnetRoot)
    {
        if (string.IsNullOrEmpty(dotnetRoot)) return false;

        var coreAppDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
        if (Directory.Exists(coreAppDir))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(coreAppDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith("8.", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
        }

        // 针对单文件/自包含等特殊目录结构
        if (File.Exists(Path.Combine(dotnetRoot, "hostfxr.dll")) && File.Exists(Path.Combine(dotnetRoot, "coreclr.dll")))
        {
            return true;
        }

        return false;
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is IOException or TimeoutException;
}

internal static class JobObjectHelper
{
    private static IntPtr _jobHandle = IntPtr.Zero;
    private static readonly object _lock = new();

    public static void TryAssignProcess(Process process)
    {
        try
        {
            EnsureJobObject();
            if (_jobHandle != IntPtr.Zero && !process.HasExited)
            {
                AssignProcessToJobObject(_jobHandle, process.Handle);
            }
        }
        catch
        {
            // Best effort; Job Object failures should never block VS or extension functionality
        }
    }

    private static void EnsureJobObject()
    {
        if (_jobHandle != IntPtr.Zero) return;
        lock (_lock)
        {
            if (_jobHandle != IntPtr.Zero) return;

            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero) return;

            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = info
            };

            var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            var extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                SetInformationJobObject(_jobHandle, 9 /* JobObjectExtendedLimitInformation */, extendedInfoPtr, (uint)length);
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }
}