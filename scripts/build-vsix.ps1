param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$Platform = "AnyCPU",

    [string]$VsixTargetFramework = "",

    [switch]$Rebuild = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot "src\VsDebugMcp.Vsix\VsDebugMcp.Vsix.csproj"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " VsDebugMcp VSIX Build Tool" -ForegroundColor Cyan
Write-Host " Configuration: $Configuration | Platform: $Platform | Rebuild: $Rebuild" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Locate vswhere.exe
$vswhereCandidates = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
)
$vswhere = $vswhereCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

# 2. Locate Visual Studio installation root and version
$vsInstallRoot = $null
$vsInstallationVersion = $null

if ($vswhere) {
    $vsInstallRoot = & $vswhere -latest -products * -property installationPath
    $vsInstallationVersion = & $vswhere -latest -products * -property installationVersion
}

if (-not $vsInstallRoot) {
    if ($env:VSINSTALLDIR -and (Test-Path $env:VSINSTALLDIR)) {
        $vsInstallRoot = $env:VSINSTALLDIR.TrimEnd('\', '/')
    }
    elseif ($env:DevEnvDir -and (Test-Path $env:DevEnvDir)) {
        $vsInstallRoot = [System.IO.Path]::GetFullPath((Join-Path $env:DevEnvDir "..\..\"))
    }
    else {
        # Standard fallback paths
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
}

if (-not $vsInstallRoot) {
    Write-Error "Could not detect a Visual Studio installation root directory. Please install Visual Studio 2026/2022 or set VSINSTALLDIR."
    exit 1
}

Write-Host "Detected Visual Studio: $vsInstallRoot" -ForegroundColor Green
if ($vsInstallationVersion) {
    Write-Host "Detected Visual Studio Version: $vsInstallationVersion" -ForegroundColor Green
}

# 3. Determine TargetFramework (vs2026_5 for VS 18.x, vs2022 for VS 17.x)
if (-not $VsixTargetFramework) {
    if ($vsInstallationVersion -and $vsInstallationVersion.StartsWith("17.")) {
        $VsixTargetFramework = "vs2022"
    }
    elseif ($vsInstallationVersion -and $vsInstallationVersion.StartsWith("18.")) {
        $VsixTargetFramework = "vs2026_5"
    }
    elseif ($vsInstallRoot -match "2022") {
        $VsixTargetFramework = "vs2022"
    }
    else {
        $VsixTargetFramework = "vs2026_5"
    }
}

Write-Host "Target Framework: $VsixTargetFramework" -ForegroundColor Green

# 4. Locate MSBuild.exe
$msbuildPath = $null

if ($vswhere) {
    # Prefer 64-bit MSBuild
    $msbuildAmd64 = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\amd64\MSBuild.exe"
    if ($msbuildAmd64 -and (Test-Path $msbuildAmd64)) {
        $msbuildPath = $msbuildAmd64
    }
    else {
        $msbuildStd = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"
        if ($msbuildStd -and (Test-Path $msbuildStd)) {
            $msbuildPath = $msbuildStd
        }
    }
}

if (-not $msbuildPath) {
    $candidates = @(
        (Join-Path $vsInstallRoot "MSBuild\Current\Bin\amd64\MSBuild.exe"),
        (Join-Path $vsInstallRoot "MSBuild\Current\Bin\MSBuild.exe")
    )
    $msbuildPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $msbuildPath) {
    $cmd = Get-Command "msbuild.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $msbuildPath = $cmd.Source
    }
}

if (-not $msbuildPath) {
    Write-Error "Could not locate MSBuild.exe in Visual Studio or PATH."
    exit 1
}

Write-Host "Using MSBuild: $msbuildPath" -ForegroundColor Green

# 5. Assemble build arguments
$target = if ($Rebuild) { "Rebuild" } else { "Build" }
$msbuildArgs = @(
    "`"$projectPath`"",
    "/restore",
    "/t:$target",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/p:DeployExtension=false",
    "/p:VsInstallRoot=`"$vsInstallRoot`"",
    "/p:VsixTargetFramework=$VsixTargetFramework",
    "/m:1",
    "/nr:false",
    "/v:minimal"
)

Write-Host "Executing MSBuild command..." -ForegroundColor Cyan
& $msbuildPath $msbuildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "MSBuild execution failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 6. Locate and verify generated VSIX package
$vsixCandidates = Get-ChildItem -Path (Join-Path $repoRoot "src\VsDebugMcp.Vsix\bin") -Recurse -Filter "*.vsix" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match [regex]::Escape($Configuration) -and $_.FullName -match [regex]::Escape($VsixTargetFramework) } |
    Sort-Object LastWriteTime -Descending

if (-not $vsixCandidates) {
    $vsixCandidates = Get-ChildItem -Path (Join-Path $repoRoot "src\VsDebugMcp.Vsix\bin") -Recurse -Filter "*.vsix" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match [regex]::Escape($Configuration) } |
        Sort-Object LastWriteTime -Descending
}

if ($vsixCandidates) {
    $vsix = $vsixCandidates[0]
    $sizeKb = [math]::Round($vsix.Length / 1KB, 2)
    $sizeMb = [math]::Round($vsix.Length / 1MB, 2)
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " VSIX Build Succeeded!" -ForegroundColor Green
    Write-Host " Output: $($vsix.FullName)" -ForegroundColor Green
    Write-Host " Size: $sizeKb KB ($sizeMb MB) | Modified: $($vsix.LastWriteTime)" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
}
else {
    Write-Warning "Build finished, but no .vsix package was found in bin directory matching Configuration '$Configuration'."
}
