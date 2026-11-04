# Release build script — Phase 7 + Velopack (2026-11-04)
# Produces: Velopack setup + packages (primary channel), portable ZIP, WiX MSI, SHA256SUMS.
# Prerequisites: .NET 10 SDK, dotnet tool install --global vpk, dotnet tool install --global wix
# Usage: .\build\release.ps1 [-Version "1.0.0"] [-Architectures "x64"]
# Version defaults to the single source of truth: <Version> in Ganjineh.Desktop.csproj.

param(
    [string]$Version = "",
    [string[]]$Architectures = @("x64"),
    [string]$OutputRoot = "$PSScriptRoot\..\artifacts\release"
)

$ErrorActionPreference = "Stop"
$ProjectPath = "$PSScriptRoot\..\src\Ganjineh.Desktop\Ganjineh.Desktop.csproj"
$RepoRoot = "$PSScriptRoot\.."

if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Get-Content $ProjectPath -Raw
    if ($csproj -match '<Version>([^<]+)</Version>') {
        $Version = $Matches[1]
    } else {
        throw "No <Version> found in Ganjineh.Desktop.csproj — pass -Version explicitly."
    }
}

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

    # Velopack: primary distribution channel (per-user install, delta updates from GitHub Releases)
    if ($arch -eq "x64") {
        $velopackDir = "$OutputRoot\velopack"
        Write-Host "`n--- Velopack pack v$Version ---"
        & "$env:USERPROFILE\.dotnet\tools\vpk.exe" pack `
            --packId Ganjineh `
            --packVersion $Version `
            --packDir $publishDir `
            --mainExe Ganjineh.exe `
            --packAuthors "Reza Noei" `
            --packTitle "Ganjineh" `
            --icon "$RepoRoot\src\Ganjineh.Desktop\Assets\Icons\ganjineh_E1_1000x1000.ico" `
            --outputDir $velopackDir
        if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
        # Setup executable is the user-facing installer; feed metadata + nupkgs ride along.
        $artifacts += (Get-ChildItem $velopackDir -Filter *.exe | Select-Object -First 1).FullName
    }

    # Portable ZIP
    $portableDir = "$OutputRoot\portable"
    New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
    $portableZip = "$portableDir\Ganjineh-Portable-$arch-$Version.zip"
    Write-Host "Creating $portableZip"
    Compress-Archive -Path "$publishDir\*" -DestinationPath $portableZip -Force
    $artifacts += $portableZip

    # Installer (WiX MSI) — machine-wide alternative channel; x64 only for now
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
$hashFile = "$OutputRoot\SHA256SUMS.txt"
New-Item -ItemType Directory -Force -Path (Split-Path $hashFile) | Out-Null
$hashLines = foreach ($a in $artifacts) {
    $hash = (Get-FileHash $a -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path $a -Leaf)"
}
$hashLines | Set-Content $hashFile -Encoding UTF8
$artifacts += $hashFile

$manifest = @{
    version = $Version
    architectures = $Architectures
    artifacts = $artifacts
    builtAt = (Get-Date).ToUniversalTime().ToString("o")
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content "$OutputRoot\release-manifest.json" -Encoding UTF8

Write-Host "`n=== Release build complete v$Version ==="
foreach ($a in $artifacts) { Write-Host "  $a" }
