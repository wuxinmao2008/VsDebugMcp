param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$Platform = "AnyCPU",

    [ValidateSet("Auto", "2026", "2022", "2019", "2017", "All")]
    [string]$TargetVs = "Auto",

    [string]$VsixTargetFramework = "",

    [switch]$Rebuild = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath2026 = Join-Path $repoRoot "src\VsDebugMcp.Vsix\VsDebugMcp.Vsix.csproj"
$projectPath2019 = Join-Path $repoRoot "src\VsDebugMcp.Vsix.2019\VsDebugMcp.Vsix.2019.csproj"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " VsDebugMcp Multi-Target VSIX Build Tool" -ForegroundColor Cyan
Write-Host " Configuration: $Configuration | Platform: $Platform | TargetVs: $TargetVs | Rebuild: $Rebuild" -ForegroundColor Cyan
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

Write-Host "Detected Host Visual Studio: $vsInstallRoot" -ForegroundColor Green
if ($vsInstallationVersion) {
    Write-Host "Detected Host Visual Studio Version: $vsInstallationVersion" -ForegroundColor Green
}

# 3. Locate MSBuild.exe
$msbuildPath = $null

if ($vswhere) {
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

# 4. Determine tasks to execute
$buildTasks = @()

if ($TargetVs -eq "2019" -or $TargetVs -eq "2017") {
    $buildTasks += @{ Name = "VS 2017 / 2019 (v15.x / v16.x)"; Project = $projectPath2019; Tfm = $null }
}
elseif ($TargetVs -eq "2022") {
    $buildTasks += @{ Name = "VS 2022 (v17.x)"; Project = $projectPath2026; Tfm = "vs2022" }
}
elseif ($TargetVs -eq "2026") {
    $buildTasks += @{ Name = "VS 2026 (v18.x)"; Project = $projectPath2026; Tfm = "vs2026_5" }
}
elseif ($TargetVs -eq "All") {
    $buildTasks += @{ Name = "VS 2026 (v18.x)"; Project = $projectPath2026; Tfm = "vs2026_5" }
    $buildTasks += @{ Name = "VS 2022 (v17.x)"; Project = $projectPath2026; Tfm = "vs2022" }
    $buildTasks += @{ Name = "VS 2017 / 2019 (v15.x / v16.x)"; Project = $projectPath2019; Tfm = $null }
}
else {
    # Auto
    $autoTfm = $VsixTargetFramework
    if (-not $autoTfm) {
        if ($vsInstallationVersion -and $vsInstallationVersion.StartsWith("17.")) {
            $autoTfm = "vs2022"
        }
        else {
            $autoTfm = "vs2026_5"
        }
    }
    $buildTasks += @{ Name = "Auto ($autoTfm)"; Project = $projectPath2026; Tfm = $autoTfm }
}

$target = if ($Rebuild) { "Rebuild" } else { "Build" }
$builtPackages = @()

foreach ($task in $buildTasks) {
    Write-Host "`n>>> Building $($task.Name)..." -ForegroundColor Magenta
    $msbuildArgs = @(
        "`"$($task.Project)`"",
        "/restore",
        "/t:$target",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/p:DeployExtension=false",
        "/p:VsInstallRoot=`"$vsInstallRoot`"",
        "/m:1",
        "/nr:false",
        "/v:minimal"
    )

    if ($task.Tfm) {
        $msbuildArgs += "/p:VsixTargetFramework=$($task.Tfm)"
    }

    & $msbuildPath $msbuildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "MSBuild execution failed for $($task.Name) with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    # Locate package
    $binDir = Split-Path (Split-Path $task.Project -Parent) -Leaf
    $searchDir = Join-Path $repoRoot "src\$binDir\bin"
    $candidates = Get-ChildItem -Path $searchDir -Recurse -Filter "*.vsix" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match [regex]::Escape($Configuration) } |
        Sort-Object LastWriteTime -Descending

    if ($candidates) {
        $builtPackages += $candidates[0]
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " VSIX Build Summary" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

foreach ($pkg in $builtPackages) {
    $sizeKb = [math]::Round($pkg.Length / 1KB, 2)
    $sizeMb = [math]::Round($pkg.Length / 1MB, 2)
    Write-Host " Package: $($pkg.Name)" -ForegroundColor Green
    Write-Host " Path:    $($pkg.FullName)" -ForegroundColor Green
    Write-Host " Size:    $sizeKb KB ($sizeMb MB) | Modified: $($pkg.LastWriteTime)" -ForegroundColor Green
    Write-Host "----------------------------------------------------------" -ForegroundColor DarkGray
}

