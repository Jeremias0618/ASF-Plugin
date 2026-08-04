#Requires -Version 5.1
<#
.SYNOPSIS
  Builds IpcConfig.dll matching ASF-BOT ArchiSteamFarm.exe version and publishes to plugins/.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -ASFTargetVersion 6.3.8.4
  .\build.ps1 -Configuration Release -OutDir "C:\path\to\ASF-BOT\plugins\IpcConfig"
#>
[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string] $Configuration = 'Release',

  [string] $OutDir = '',

  [string] $ASFTargetVersion = ''
)

$ErrorActionPreference = 'Stop'

$PluginSrc = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $PluginSrc '..\..\..')).Path
$Csproj = Join-Path $PluginSrc 'IpcConfig.csproj'
$AsfBotExe = Join-Path $RepoRoot 'ASF-BOT\ArchiSteamFarm.exe'

if (-not $OutDir) {
  $OutDir = Join-Path $RepoRoot 'ASF-BOT\plugins\IpcConfig'
}

if (-not $ASFTargetVersion) {
  if (Test-Path $AsfBotExe) {
    $ASFTargetVersion = (Get-Item $AsfBotExe).VersionInfo.FileVersion
    Write-Host "Detected ASF-BOT version: $ASFTargetVersion" -ForegroundColor Cyan
  } else {
    $ASFTargetVersion = '6.3.8.4'
    Write-Host "ASF-BOT exe not found; using default ASFTargetVersion=$ASFTargetVersion" -ForegroundColor Yellow
  }
}

$PublishDir = Join-Path $PluginSrc "bin\publish\$Configuration"

Write-Host "Building IpcConfig ($Configuration) against ArchiSteamFarm $ASFTargetVersion..." -ForegroundColor Cyan
dotnet publish $Csproj -c $Configuration -o $PublishDir --nologo `
  -p:ASFTargetVersion=$ASFTargetVersion
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$files = @(
  'IpcConfig.dll',
  'IpcConfig.deps.json',
  'IpcConfig.pdb'
)

# Only ship the plugin assembly — never copy System.Composition.* etc. into plugins/
foreach ($name in $files) {
  $src = Join-Path $PublishDir $name
  if (Test-Path $src) {
    Copy-Item -Force $src (Join-Path $OutDir $name)
    Write-Host "  -> $OutDir\$name"
  }
}

Get-ChildItem $OutDir -File | Where-Object { $_.Name -notlike 'IpcConfig*' } | ForEach-Object {
  Write-Host "Removing unexpected $($_.Name)" -ForegroundColor Yellow
  Remove-Item -Force $_.FullName
}

Write-Host ""
Write-Host "Done. Restart ArchiSteamFarm.exe so ASF loads the plugin." -ForegroundColor Green
Write-Host "Target: $OutDir (compiled for ASF $ASFTargetVersion)"
Write-Host "Expect log line: IpcConfig ... loaded. Endpoints: GET|PUT|DELETE /Api/IpcConfig"
