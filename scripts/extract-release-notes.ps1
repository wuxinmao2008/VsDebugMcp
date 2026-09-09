param(
    [string]$Version,
    [string]$ChangelogPath = "CHANGELOG.md",
    [string]$OutputPath = "RELEASE_NOTES.md"
)

$ErrorActionPreference = "Stop"

if (-not $Version) {
    Write-Error "Version parameter is required (e.g. '0.1.19.0' or 'v0.1.19.0')."
    exit 1
}

# Strip leading 'v' or 'V' if present
$cleanVersion = $Version.TrimStart('v', 'V').Trim()

if (-not (Test-Path $ChangelogPath)) {
    Write-Error "Changelog file not found at '$ChangelogPath'."
    exit 1
}

$lines = Get-Content -Path $ChangelogPath
$extracting = $false
$notes = @()

foreach ($line in $lines) {
    if ($line -match "^##\s+\[$([regex]::Escape($cleanVersion))\]") {
        $extracting = $true
        continue
    }
    elseif ($extracting -and $line -match "^##\s+\[") {
        break
    }

    if ($extracting) {
        $notes += $line
    }
}

$notesText = ($notes -join "`n").Trim()

if (-not $notesText) {
    Write-Warning "No release notes found for version '$cleanVersion' in '$ChangelogPath'. Generating fallback notes."
    $notesText = "Release $cleanVersion of VsDebugMcp.`n`nPlease see [CHANGELOG.md](CHANGELOG.md) for detailed changes."
}

Set-Content -Path $OutputPath -Value $notesText -Encoding utf8
Write-Host "Successfully extracted release notes for version '$cleanVersion' to '$OutputPath' ($($notesText.Length) chars)." -ForegroundColor Green
