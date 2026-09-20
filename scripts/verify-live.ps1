# Runs the live verification harness against real Mojang services.
# Usage: ./scripts/verify-live.ps1 [version] [--seconds n] [--root path]
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $arguments = @('run', '--project', 'tools/Ferrite.Verify', '--') + $args
    Write-Output "Ferrite live verification: $($args -join ' ')"
    & dotnet @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
