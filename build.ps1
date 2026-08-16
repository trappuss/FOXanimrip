# SPDX-License-Identifier: MIT
# Build foxanimrip.  Usage:  .\build.ps1 -FoxBrowser "C:\Tools\FoxBrowser\FoxBrowser.exe"
param(
    [Parameter(Mandatory = $true)][string]$FoxBrowser,
    [string]$Configuration = "Release",
    [string]$Output = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$refs = Join-Path $root "refs"

Write-Host "Unpacking reference assemblies from $FoxBrowser"
python (Join-Path $root "tools/extract-refs.py") $FoxBrowser $refs

Write-Host "Building GUI + CLI (foxanimrip.exe)"
dotnet publish (Join-Path $root "src/FoxAnimRip/FoxAnimRip.csproj") `
    -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:FoxBrowserRefDir=$refs `
    -o (Join-Path $root $Output)

Write-Host "Building console-only build (foxanimrip-cli.exe)"
dotnet publish (Join-Path $root "src/FoxAnimRip.Headless/FoxAnimRip.Headless.csproj") `
    -c $Configuration -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:FoxBrowserRefDir=$refs `
    -o (Join-Path $root $Output)

Write-Host ""
Write-Host "Done. Binaries in $(Join-Path $root $Output)"
