param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# 1. Check if Visual Studio Experimental Instance is running
$expVs = Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" | Where-Object { $_.CommandLine -match 'RootSuffix.*Exp' }
if ($expVs) {
    Write-Error "Detected running Visual Studio Experimental Instance (PID: $($expVs.ProcessId)). Please close it first to release DLL locks!"
    exit 1
}

# 2. Check and stop orphan VsDebugMcp.Host processes
$hosts = Get-Process -Name "VsDebugMcp.Host" -ErrorAction SilentlyContinue
if ($hosts) {
    Write-Host "Stopping existing VsDebugMcp.Host processes..." -ForegroundColor Yellow
    $hosts | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 3. Locate compiled VSIX package (select newest)
$candidates = @(
    (Join-Path $PSScriptRoot "..\src\VsDebugMcp.Vsix\bin\x64\$Configuration\vs2026_5\VsDebugMcp.Vsix.vsix"),
    (Join-Path $PSScriptRoot "..\src\VsDebugMcp.Vsix\bin\$Configuration\vs2026_5\VsDebugMcp.Vsix.vsix")
) | Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTime } -Descending

if (-not $candidates) {
    Write-Error "VSIX package not found. Please build the project first!"
    exit 1
}
$vsixPath = $candidates[0]
Write-Host "Using VSIX package: $vsixPath (LastWrite: $((Get-Item $vsixPath).LastWriteTime))" -ForegroundColor Cyan

# 4. Locate experimental instance extension directory
$expBasePath = "$env:LOCALAPPDATA\Microsoft\VisualStudio"
$targetDll = Get-ChildItem -Path $expBasePath -Recurse -Filter "VsDebugMcp.Vsix.dll" -ErrorAction SilentlyContinue | 
             Where-Object { $_.FullName -match '\\18\.0_[^\\\\]+Exp\\extensions\\' } | 
             Select-Object -First 1

if (-not $targetDll) {
    Write-Error "Could not find installed VsDebugMcp extension in Experimental Instance! Please install via VSIX once."
    exit 1
}

$deployDir = $targetDll.DirectoryName
Write-Host "Target deployment directory: $deployDir" -ForegroundColor Cyan

# 5. Extract latest VSIX to directory
Write-Host "Extracting latest VSIX package..." -ForegroundColor Green
tar.exe -xf $vsixPath -C $deployDir

# 6. Touch configurationchanged file to trigger VS cache refresh
$extensionsDir = Split-Path $deployDir -Parent
$stampFile = Join-Path $extensionsDir "extensions.configurationchanged"
Set-Content -Path $stampFile -Value (Get-Date).ToString("o")

# 7. Verify deployed versions
$deployedVsixDll = Join-Path $deployDir "VsDebugMcp.Vsix.dll"
$deployedHostExe = Join-Path $deployDir "Host\VsDebugMcp.Host.exe"

$vsixVer = (Get-Item $deployedVsixDll).VersionInfo.ProductVersion
$hostVer = (Get-Item $deployedHostExe).VersionInfo.ProductVersion

Write-Host "========================================" -ForegroundColor Green
Write-Host "VSIX Extension Deployed Successfully!" -ForegroundColor Green
Write-Host "  VsDebugMcp.Vsix.dll Version: $vsixVer" -ForegroundColor White
Write-Host "  VsDebugMcp.Host.exe Version: $hostVer" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Green
