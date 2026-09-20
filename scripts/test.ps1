# Runs every test project.
#
# The .NET 10 SDK's `dotnet test` Microsoft.Testing.Platform integration does not discover xunit v3
# tests in this repository (it reports "Zero tests ran"), so this script uses the runner's native
# entry point instead, which is what the SDK invokes under the hood.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $configuration = if ($args.Count -ge 1) { $args[0] } else { 'Debug' }
    $projects = @(
        'tests/Ferrite.Core.Tests',
        'tests/Ferrite.App.Tests'
    )

    $failed = 0
    foreach ($project in $projects) {
        Write-Output "Testing $project ($configuration)"
        & dotnet run --project $project -c $configuration
        if ($LASTEXITCODE -ne 0) {
            $failed++
            Write-Output "FAILED: $project (exit $LASTEXITCODE)"
        }
    }

    if ($failed -gt 0) {
        Write-Output "$failed test project(s) failed."
        exit 1
    }

    Write-Output 'All test projects passed.'
}
finally {
    Pop-Location
}
