param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# 1. 检查 Visual Studio 实验实例是否已关闭
$expVs = Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" | Where-Object { $_.CommandLine -match 'RootSuffix.*Exp' }
if ($expVs) {
    Write-Error "检测到 Visual Studio 实验实例正在运行 (PID: $($expVs.ProcessId))。请先关闭实验实例以解除 DLL 文件锁定！"
    exit 1
}

# 2. 检查并关闭残留的 VsDebugMcp.Host 进程
$hosts = Get-Process -Name "VsDebugMcp.Host" -ErrorAction SilentlyContinue
if ($hosts) {
    Write-Host "正在关闭残留的 VsDebugMcp.Host 进程..." -ForegroundColor Yellow
    $hosts | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 3. 确认已编译的 VSIX 包
$vsixPath = Join-Path $PSScriptRoot "..\src\VsDebugMcp.Vsix\bin\$Configuration\vs2026_5\VsDebugMcp.Vsix.vsix"
if (-not (Test-Path $vsixPath)) {
    Write-Error "未找到已生成的 VSIX 包: $vsixPath。请先构建项目！"
    exit 1
}

# 4. 定位实验实例的扩展安装目录
$expBasePath = "$env:LOCALAPPDATA\Microsoft\VisualStudio"
$targetDll = Get-ChildItem -Path $expBasePath -Recurse -Filter "VsDebugMcp.Vsix.dll" -ErrorAction SilentlyContinue | 
             Where-Object { $_.FullName -match '\\18\.0_[^\\\\]+Exp\\extensions\\' } | 
             Select-Object -First 1

if (-not $targetDll) {
    Write-Error "未在 Visual Studio 实验实例的扩展目录中找到已安装的 VsDebugMcp 扩展！请先通过 VSIX 安装一次。"
    exit 1
}

$deployDir = $targetDll.DirectoryName
Write-Host "目标扩展部署目录: $deployDir" -ForegroundColor Cyan

# 5. 解压部署最新的 VSIX 包覆盖到该目录
Write-Host "正在覆盖部署最新的 VSIX 扩展文件..." -ForegroundColor Green
tar.exe -xf $vsixPath -C $deployDir

# 6. 刷新 VS 扩展配置变更时间戳以触发缓存更新
$extensionsDir = Split-Path $deployDir -Parent
$stampFile = Join-Path $extensionsDir "extensions.configurationchanged"
Set-Content -Path $stampFile -Value (Get-Date).ToString("o")

# 7. 打印并验证部署后的文件版本
$deployedVsixDll = Join-Path $deployDir "VsDebugMcp.Vsix.dll"
$deployedHostExe = Join-Path $deployDir "Host\VsDebugMcp.Host.exe"

$vsixVer = (Get-Item $deployedVsixDll).VersionInfo.ProductVersion
$hostVer = (Get-Item $deployedHostExe).VersionInfo.ProductVersion

Write-Host "========================================" -ForegroundColor Green
Write-Host "VSIX 扩展部署成功！" -ForegroundColor Green
Write-Host "  VsDebugMcp.Vsix.dll 版本: $vsixVer" -ForegroundColor White
Write-Host "  VsDebugMcp.Host.exe 版本: $hostVer" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Green
