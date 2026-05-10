<#
.SYNOPSIS
  Pack the unmanaged solution under .\src into .\bin\CoutureTaxRates.zip.

.DESCRIPTION
  Wraps `pac solution pack` so anyone with the Power Platform CLI installed
  can produce a deployable ZIP from a fresh clone:

      cd solutions\CoutureTaxRates
      .\build.ps1

  Requires the Power Platform CLI: https://aka.ms/PowerPlatformCLI
#>

[CmdletBinding()]
param(
    [ValidateSet("Unmanaged", "Managed", "Both")]
    [string]$PackageType = "Unmanaged"
)

$ErrorActionPreference = "Stop"

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $here "src"
$bin  = Join-Path $here "bin"
$zip  = Join-Path $bin  "CoutureTaxRates.zip"

if (-not (Get-Command pac -ErrorAction SilentlyContinue)) {
    throw "Power Platform CLI ('pac') not found on PATH. Install from https://aka.ms/PowerPlatformCLI."
}

if (-not (Test-Path $bin)) { New-Item -ItemType Directory -Path $bin | Out-Null }
if (Test-Path $zip)        { Remove-Item $zip -Force }

Write-Host "Packing $src -> $zip ($PackageType)" -ForegroundColor Cyan
pac solution pack `
    --zipfile $zip `
    --folder $src `
    --packagetype $PackageType

if ($LASTEXITCODE -ne 0) { throw "pac solution pack failed with exit code $LASTEXITCODE." }

Write-Host "Done. Import $zip via make.powerapps.com -> Solutions -> Import." -ForegroundColor Green
