param([switch]$Background)
$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
$orbitExe = Join-Path $orbitRoot 'dist\Orbit\Orbit.exe'
if (-not (Test-Path -LiteralPath $orbitExe)) { $orbitExe = Join-Path $orbitRoot 'src\Orbit.App\bin\Release\net10.0-windows\Orbit.exe' }
if (-not (Test-Path -LiteralPath $orbitExe)) { & (Join-Path $PSScriptRoot 'build.ps1') }
if ($Background) { Start-Process -FilePath $orbitExe -ArgumentList '--background' -WindowStyle Hidden }
else { Start-Process -FilePath $orbitExe }
