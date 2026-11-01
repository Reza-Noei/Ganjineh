# Release build script — Phase 7
# Produces self-contained publish, portable ZIP, WiX installer, hashes, and manifest.
# Prerequisites: .NET 10 SDK, WiX v5 dotnet tool (dotnet tool install --global wix), 7z or Compress-Archive.
#Usage: .\build\release.ps1 [-Version "1.0.0"] [-Architectures "x64","arm64"]

param(
    [string]$Version = "1.0.0",
    [string[]]$Architectures = @("x64"),
    [string]$OutputRoot = "$PSScriptRoot\..\artifacts\release"
)

$ErrorActionPreference = "Stop"
$ProjectPath = "$PSScriptRoot\..\src\Ganjineh.Desktop\Ganjineh.Desktop.csproj"
$RepoRoot = "$PSScriptRoot\.."

Write-Host "=== Ganjineh Release Build v$Version ==="

$artifacts = @()
foreach ($arch in $Architectures) {
    $rid = "win-$arch"
    $publishDir = "$RepoRoot\artifacts\publish\$rid"
    Write-Host "`n--- Publishing $rid (self-contained) ---"

    dotnet publish $ProjectPath `
        -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }

    # Portable ZIP
    $portableDir = "$OutputRoot\portable"
    New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
    $portableZip = "$portableDir\Ganjineh-Portable-$arch-$Version.zip"
    Write-Host "Creating $portableZip"
    Compress-Archive -Path "$publishDir\*" -DestinationPath $portableZip -Force
    $artifacts += $portableZip

    # Installer (WiX) — x64 only for now
    if ($arch -eq "x64") {
        $installerDir = "$OutputRoot\installer"
        New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
        $msiPath = "$installerDir\Ganjineh-Setup-$arch-$Version.msi"
        Write-Host "Building $msiPath"
        wix build "$RepoRoot\installer\Ganjineh.Installer.wxs" `
            -arch x64 `
            -o $msiPath `
            -d "PublishDir=$publishDir"
        if ($LASTEXITCODE -ne 0) { throw "WiX build failed" }
        $artifacts += $msiPath
    }
}

# Hashes
$manifestDir = "$OutputRoot\manifests"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
$hashFile = "$OutputRoot\..\checksums\SHA256SUMS.txt"
New-Item -ItemType Directory -Force -Path (Split-Path $hashFile) | Out-Null
$hashLines = foreach ($a in $artifacts) {
    $hash = (Get-FileHash $a -Algorithm SHA256).Hash.ToLower()
    "$(hash)  $(Split-Path $a -Leaf)"
}
$hashLines | Set-Content $hashFile -Encoding UTF8

$manifest = @{
    version = $Version
    architectures = $Architectures
    artifacts = $artifacts
    builtAt = (Get-Date).ToUniversalTime().ToString("o")
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content "$OutputRoot\manifests\release-manifest.json" -Encoding UTF8

Write-Host "`n=== Release build complete ==="
foreach ($a in $artifacts) { Write-Host "  $a" }
