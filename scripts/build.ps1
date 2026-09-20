$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $configuration = if ($args.Count -ge 1) { $args[0] } else { 'Debug' }
    Write-Output "Building Ferrite ($configuration)"
    dotnet build Ferrite.slnx -c $configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Output 'Build succeeded.'
}
finally {
    Pop-Location
}
