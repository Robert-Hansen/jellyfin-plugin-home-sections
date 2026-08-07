<#
.SYNOPSIS
  Build Home Screen Sections plugin zip(s) for manual install on Jellyfin.

.DESCRIPTION
  Produces Release-<jellyfinVersion>.zip files matching the official plugin
  package layout (dll + deps.json + logo.png [+ pdb]).

.PARAMETER PluginVersion
  Assembly/plugin version, e.g. 2.5.12.0

.PARAMETER JellyfinVersions
  One or more Jellyfin server versions to target.

.PARAMETER OutputDir
  Directory for zip files (default: .\dist)

.EXAMPLE
  .\build-release.ps1 -PluginVersion 2.5.12.0 -JellyfinVersions 10.11.11

.EXAMPLE
  .\build-release.ps1 -PluginVersion 2.5.12.0 -JellyfinVersions 10.10.7,10.11.5,10.11.11
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $PluginVersion = "2.5.12.0",

    [Parameter(Mandatory = $false)]
    [string[]] $JellyfinVersions = @("10.11.11"),

    [Parameter(Mandatory = $false)]
    [string] $OutputDir
)

$ErrorActionPreference = "Stop"

$RepoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "dist"
}

# Allow comma-separated string: -JellyfinVersions "10.10.7,10.11.11"
if ($JellyfinVersions.Count -eq 1 -and $JellyfinVersions[0] -match ",") {
    $JellyfinVersions = $JellyfinVersions[0].Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

if ($PluginVersion -match '^\d+\.\d+\.\d+$') {
    $PluginVersion = "$PluginVersion.0"
}

$project = Join-Path $RepoRoot "src\Jellyfin.Plugin.HomeScreenSections\Jellyfin.Plugin.HomeScreenSections.csproj"
$assemblyInfo = Join-Path $RepoRoot "src\Jellyfin.Plugin.HomeScreenSections\Properties\AssemblyInfo.cs"
$csprojText = Get-Content -Raw $project
$assemblyInfoText = Get-Content -Raw $assemblyInfo

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Temporarily stamp versions (restored in finally)
try {
    $newAssemblyInfo = $assemblyInfoText -replace 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(`"$PluginVersion`")"
    Set-Content -Path $assemblyInfo -Value $newAssemblyInfo -NoNewline

    $newCsproj = $csprojText -replace '<Version>[^<]*</Version>', "<Version>$PluginVersion</Version>"
    Set-Content -Path $project -Value $newCsproj -NoNewline

    Write-Host "Plugin version: $PluginVersion" -ForegroundColor Cyan

    foreach ($jf in $JellyfinVersions) {
        Write-Host "`n=== Building for Jellyfin $jf ===" -ForegroundColor Cyan

        if ($jf -like "10.10.*") {
            $tfm = "net8.0"
        } else {
            $tfm = "net9.0"
        }

        & dotnet restore $project -p:JellyfinVersion=$jf
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for $jf" }

        & dotnet build $project -c Release --no-restore -p:JellyfinVersion=$jf
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $jf" }

        $outDir = Join-Path $RepoRoot "src\Jellyfin.Plugin.HomeScreenSections\bin\Release\$tfm"
        $stage = Join-Path $env:TEMP "hss-stage-$jf"
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $stage | Out-Null

        $required = @(
            "Jellyfin.Plugin.HomeScreenSections.dll",
            "Jellyfin.Plugin.HomeScreenSections.deps.json",
            "logo.png"
        )
        foreach ($file in $required) {
            $src = Join-Path $outDir $file
            if (-not (Test-Path $src)) { throw "Missing build output: $src" }
            Copy-Item $src $stage
        }

        $pdb = Join-Path $outDir "Jellyfin.Plugin.HomeScreenSections.pdb"
        if (Test-Path $pdb) {
            Copy-Item $pdb $stage
        }

        $zipPath = Join-Path $OutputDir "Release-$jf.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

        Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -Force
        Write-Host "Created $zipPath" -ForegroundColor Green
    }

    Write-Host "`nDone. Install on Jellyfin:" -ForegroundColor Green
    Write-Host "  1. Stop Jellyfin (or be ready to restart)"
    Write-Host "  2. Unzip Release-<your-version>.zip into:"
    Write-Host "       <Jellyfin data>/plugins/HomeScreenSections/"
    Write-Host "     e.g. C:\ProgramData\Jellyfin\Server\plugins\HomeScreenSections\"
    Write-Host "  3. Restart Jellyfin"
    Write-Host ""
    Write-Host "Requires File Transformation + Plugin Pages plugins (see README)."
}
finally {
    Set-Content -Path $assemblyInfo -Value $assemblyInfoText -NoNewline
    Set-Content -Path $project -Value $csprojText -NoNewline
}
