param(
    [ValidateSet("x64-Debug", "x64-Release")]
    [string]$Preset = "x64-Debug",

    [string]$OutputPath = "",

    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$cmakeRoot = Join-Path $repoRoot "sample\SampleCMake"
$buildDir = Join-Path $cmakeRoot "out\build\$Preset"
$binDir = Join-Path $buildDir "src\app"
$exePath = Join-Path $binDir "SampleCMakeApp.exe"
$pdbPath = Join-Path $binDir "SampleCMakeApp.pdb"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $buildDir "crash_sample.dmp"
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " VsDebugMcp Sample Minidump (.dmp) Generator" -ForegroundColor Cyan
Write-Host " Preset: $Preset | SkipBuild: $SkipBuild" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not $SkipBuild -or -not (Test-Path $exePath)) {
    # Ensure cmake.exe and cl.exe are available; initialize VS DevShell if needed
    $hasCl = $null -ne (Get-Command "cl.exe" -ErrorAction SilentlyContinue)
    $hasCmake = $null -ne (Get-Command "cmake.exe" -ErrorAction SilentlyContinue)

    if (-not $hasCl -or -not $hasCmake) {
        $vswhereCandidates = @(
            "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
            "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
        )
        $vswhere = $vswhereCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        $vsInstallRoot = $null

        if ($vswhere) {
            $vsInstallRoot = & $vswhere -latest -products * -property installationPath
        }

        if (-not $vsInstallRoot) {
            $fallbacks = @(
                "C:\Program Files\Microsoft Visual Studio\18\Professional",
                "C:\Program Files\Microsoft Visual Studio\18\Enterprise",
                "C:\Program Files\Microsoft Visual Studio\18\Community",
                "C:\Program Files\Microsoft Visual Studio\2022\Enterprise",
                "C:\Program Files\Microsoft Visual Studio\2022\Professional",
                "C:\Program Files\Microsoft Visual Studio\2022\Community"
            )
            $vsInstallRoot = $fallbacks | Where-Object { Test-Path $_ } | Select-Object -First 1
        }

        if (-not $vsInstallRoot) {
            throw "Could not locate Visual Studio installation to initialize CMake/MSVC x64 environment."
        }

        $devShellScript = Join-Path $vsInstallRoot "Common7\Tools\Launch-VsDevShell.ps1"
        if (-not (Test-Path $devShellScript)) {
            throw "Launch-VsDevShell.ps1 not found at: $devShellScript"
        }

        Write-Host "Initializing Visual Studio x64 Developer Environment from: $vsInstallRoot" -ForegroundColor DarkCyan
        & $devShellScript -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null
    }

    Push-Location $cmakeRoot
    try {
        if (-not (Test-Path (Join-Path $buildDir "build.ninja"))) {
            Write-Host "Configuring SampleCMake ($Preset)..." -ForegroundColor Yellow
            & cmake --preset $Preset
            if ($LASTEXITCODE -ne 0) {
                throw "cmake --preset $Preset failed with exit code $LASTEXITCODE"
            }
        }

        Write-Host "Building SampleCMakeApp ($Preset)..." -ForegroundColor Yellow
        & cmake --build --preset $Preset --target SampleCMakeApp
        if ($LASTEXITCODE -ne 0) {
            throw "cmake --build --preset $Preset --target SampleCMakeApp failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path $exePath)) {
    throw "SampleCMakeApp.exe was not found at: $exePath"
}

$outputDir = Split-Path $OutputPath -Parent
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

Write-Host "Generating Minidump via SampleCMakeApp.exe --generate-dmp..." -ForegroundColor Yellow
& $exePath --generate-dmp $OutputPath
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0 -or -not (Test-Path $OutputPath)) {
    throw "Failed to generate Minidump (exit code: $exitCode)."
}

$dmpInfo = Get-Item $OutputPath
$headerBytes = [System.IO.File]::ReadAllBytes($OutputPath)[0..3]
$magic = [System.Text.Encoding]::ASCII.GetString($headerBytes)

Write-Host ""
Write-Host "Minidump generated successfully!" -ForegroundColor Green
Write-Host "  DMP Path : $($dmpInfo.FullName) ($($dmpInfo.Length) bytes, Magic: '$magic')" -ForegroundColor Green
Write-Host "  EXE Path : $exePath" -ForegroundColor Gray
Write-Host "  PDB Path : $pdbPath" -ForegroundColor Gray
Write-Host ""
Write-Host "To debug in Visual Studio:" -ForegroundColor Cyan
Write-Host "  devenv.exe `"$($dmpInfo.FullName)`"" -ForegroundColor White
