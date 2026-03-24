#!/usr/bin/env powershell
# Build script for Spark installer
# This script compiles the NSIS script into a professional Windows installer

param(
    [string]$BuildConfig = "Debug",
    [switch]$Release,
  [switch]$Help,
  [string]$NsisPathOverride,
  [string]$Runtime = "win-x64"
)

if ($Help) {
    Write-Host @"
Spark Installer Build Script

Usage:
  .\Build-Installer.ps1 [options]

Options:
  -BuildConfig <config>  Build configuration (Debug or Release) [Default: Debug]
  -Release               Use Release build configuration
  -Help                  Show this help message

Examples:
  .\Build-Installer.ps1                    # Build installer with Debug config
  .\Build-Installer.ps1 -Release           # Build installer with Release config
  .\Build-Installer.ps1 -BuildConfig Release

Output:
  SparkInstaller.exe     Professional Windows installer

Requirements:
  - NSIS 3.x installed (https://nsis.sourceforge.io/)
  - .NET 10 SDK (for building the app)
  - MUI2 plugin for NSIS (usually included)

Setup Instructions:
  1. Download NSIS from https://nsis.sourceforge.io/
  2. Install NSIS to default location (C:\Program Files (x86)\NSIS)
  3. Run: dotnet publish -c Release -r win-x64 -p:SelfContained=true
  4. Run: .\Build-Installer.ps1 -Release
  5. Output: SparkInstaller.exe in project root

"@
    exit 0
}

# Determine build configuration
$config = if ($Release) { "Release" } else { $BuildConfig }
$installerName = "SparkInstaller-$Runtime.exe"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "Spark Installer Build Script" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Publish self-contained application
Write-Host "[1/4] Publishing Spark application ($config, $Runtime, self-contained)..." -ForegroundColor Yellow
$buildOutput = dotnet publish .\Spark.csproj -c $config -r $Runtime -p:SelfContained=true -p:PublishSingleFile=false 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-Host "ERROR: Failed to publish Spark application" -ForegroundColor Red
    Write-Host $buildOutput
    exit 1
}
Write-Host "[OK] Publish successful" -ForegroundColor Green

# Step 2: Check for NSIS installation
Write-Host "[2/4] Checking NSIS installation..." -ForegroundColor Yellow

$candidatePaths = @()
if ($NsisPathOverride) {
  $candidatePaths += $NsisPathOverride
}

$programFilesX86 = ${env:ProgramFiles(x86)}
if ($programFilesX86) {
  $candidatePaths += (Join-Path $programFilesX86 "NSIS\makensis.exe")
}

if ($env:ProgramFiles) {
  $candidatePaths += (Join-Path $env:ProgramFiles "NSIS\makensis.exe")
}

$nsisPath = $candidatePaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $nsisPath) {
    Write-Host "ERROR: NSIS not found!" -ForegroundColor Red
    Write-Host "Please install NSIS from: https://nsis.sourceforge.io/" -ForegroundColor Yellow
  Write-Host 'Expected path example: C:\Program Files (x86)\NSIS\makensis.exe' -ForegroundColor Yellow
  Write-Host "You can also pass -NsisPathOverride <full-path-to-makensis.exe>." -ForegroundColor Yellow
    exit 1
}
Write-Host "[OK] NSIS found at: $nsisPath" -ForegroundColor Green

# Step 3: Verify publish artifacts exist
Write-Host "[3/4] Verifying publish artifacts..." -ForegroundColor Yellow

$publishPath = ".\bin\$($config)\net10.0-windows\$Runtime\publish"
if (-not (Test-Path "$publishPath\Spark.exe")) {
  Write-Host "ERROR: Publish artifacts not found at $publishPath" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Publish artifacts verified" -ForegroundColor Green

# Step 4: Compile NSIS script
Write-Host "[4/4] Compiling NSIS installer..." -ForegroundColor Yellow

$nsiScript = ".\Installer\Spark.nsi"
if (-not (Test-Path $nsiScript)) {
    Write-Host "ERROR: NSIS script not found: $nsiScript" -ForegroundColor Red
    exit 1
}

& $nsisPath /V2 "/DBUILD_CONFIG=$config" "/DRUNTIME_ID=$Runtime" "/DPUBLISH_DIR=..\\bin\\$config\\net10.0-windows\\$Runtime\\publish" "/DINSTALLER_NAME=$installerName" $nsiScript
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NSIS compilation failed" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] NSIS compilation successful" -ForegroundColor Green
Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Installer created successfully!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output file: $installerName" -ForegroundColor Green
Write-Host "Installation path: C:\Program Files\Spark" -ForegroundColor Green
Write-Host ""
Write-Host "You can now distribute $installerName to users." -ForegroundColor Cyan
Write-Host ""
