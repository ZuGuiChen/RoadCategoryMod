<#
.SYNOPSIS
    Build RoadCategoryMod and optionally deploy it to the local mods folder.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Deploy
    .\build.ps1 -Deploy -GameDir "D:\Steam\steamapps\common\Cities_Skylines" -HarmonyDir "D:\Steam\steamapps\workshop\content\255710\2040656402"
#>
param(
    [switch]$Deploy,
    [string]$Configuration = "Release",
    [string]$GameDir = "",
    [string]$HarmonyDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "RoadCategoryMod.csproj"
$outDir = Join-Path $root ("bin\" + $Configuration)

# 1) locate MSBuild (prefer vswhere)
$msbuild = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($installPath) {
        $candidate = Join-Path $installPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}
if (-not $msbuild) {
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $msbuild = $c; break } }
}
if (-not $msbuild) {
    throw "MSBuild.exe not found. Install Visual Studio with the .NET desktop workload."
}

# 2) build
$msbuildArgs = @($proj, "/t:Rebuild", "/p:Configuration=$Configuration", "/v:minimal", "/nologo")
if ($GameDir) { $msbuildArgs += "/p:GameDir=$GameDir" }
if ($HarmonyDir) { $msbuildArgs += "/p:HarmonyDir=$HarmonyDir" }

Write-Host "Building with: $msbuild" -ForegroundColor Cyan
& $msbuild @msbuildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)" }

$dll = Join-Path $outDir "RoadCategoryMod.dll"
if (-not (Test-Path $dll)) { throw "Built assembly not found: $dll" }
$version = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
Write-Host "Build OK: $dll (v$version)" -ForegroundColor Green

# 3) optional deploy
if ($Deploy) {
    if (Get-Process -Name Cities -ErrorAction SilentlyContinue) {
        throw "Cities.exe is running; close the game before deploying (the DLL is locked)."
    }

    $modDir = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\RoadCategoryMod"
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $modDir "Icons") | Out-Null

    Copy-Item $dll $modDir -Force
    $pdb = Join-Path $outDir "RoadCategoryMod.pdb"
    if (Test-Path $pdb) { Copy-Item $pdb $modDir -Force }

    $icons = Join-Path $outDir "Icons"
    if (Test-Path $icons) { Copy-Item (Join-Path $icons "*.png") (Join-Path $modDir "Icons") -Force }

    Write-Host "Deployed to: $modDir" -ForegroundColor Green
}
