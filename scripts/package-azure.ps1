param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts\publish",
    [string]$ZipPath = "artifacts\rtfm26.zip"
)

$ErrorActionPreference = "Stop"

Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "rtfm26.csproj"
$resolvedOutputDir = Join-Path $repoRoot $OutputDir
$resolvedZipPath = Join-Path $repoRoot $ZipPath

Write-Host "Cleaning output directories..."
if (Test-Path $resolvedOutputDir) { Remove-Item $resolvedOutputDir -Recurse -Force }
if (Test-Path $resolvedZipPath) { Remove-Item $resolvedZipPath -Force }

$zipDir = Split-Path -Parent $resolvedZipPath
if (-not (Test-Path $zipDir)) { New-Item -ItemType Directory -Path $zipDir | Out-Null }

Write-Host "Publishing app..."
dotnet publish $projectPath -c $Configuration -o $resolvedOutputDir

Write-Host "Creating deployment zip..."
Compress-Archive -Path "$resolvedOutputDir\*" -DestinationPath $resolvedZipPath -Force

Write-Host "Validating zip layout..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($resolvedZipPath)
try {
    $badEntry = $zip.Entries | Where-Object { $_.FullName -like "publish/*" -or $_.FullName -like "artifacts/*" } | Select-Object -First 1
    if ($null -ne $badEntry) {
        throw "Zip contains an unexpected wrapper folder entry: $($badEntry.FullName)"
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Done."
Write-Host "Package: $resolvedZipPath"
