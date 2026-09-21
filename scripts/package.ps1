# Produces distributable Windows builds of Ferrite.
#
# Two shapes are produced, because they answer different questions:
#   framework-dependent - small, needs the .NET 10 desktop runtime installed
#   self-contained      - large, runs on a machine with no .NET installed
#
# Usage:
#   ./scripts/package.ps1                       # both, into artifacts/
#   ./scripts/package.ps1 -Only self-contained  # one of them
#   ./scripts/package.ps1 -Runtime win-arm64    # a different runtime identifier
#
# The result is reproducible from a clean checkout: the script deletes the publish
# directory, builds from source, and never copies user data or downloaded runtimes.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [ValidateSet('both', 'framework-dependent', 'self-contained')]
    [string]$Only = 'both',
    [string]$OutputDirectory,
    [string]$Version,
    [switch]$NoArchive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Ferrite.App/Ferrite.App.csproj'
if (-not (Test-Path -LiteralPath $project)) {
    throw "Application project not found at $project"
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

# Only ever clear a directory this script owns.
$allowedRoot = [System.IO.Path]::GetFullPath($root)
if (-not $OutputDirectory.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside the repository: $OutputDirectory"
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

$versionArgument = @()
if ($Version) {
    $versionArgument = @("-p:FerriteVersion=$Version")
}

$shapes = switch ($Only) {
    'both' { @('framework-dependent', 'self-contained') }
    default { @($Only) }
}

$results = @()

foreach ($shape in $shapes) {
    $publishDirectory = Join-Path $OutputDirectory $shape
    Write-Output "Publishing $shape ($Runtime, $Configuration)"

    $arguments = @(
        'publish', $project,
        '-c', $Configuration,
        '-r', $Runtime,
        '-o', $publishDirectory,
        '--nologo',
        '-v', 'quiet'
    ) + $versionArgument

    if ($shape -eq 'self-contained') {
        $arguments += @('--self-contained', 'true', '-p:PublishSingleFile=false')
    }
    else {
        $arguments += @('--self-contained', 'false')
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $shape (exit $LASTEXITCODE)"
    }

    $executable = Join-Path $publishDirectory 'Ferrite.exe'
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The published output has no Ferrite.exe: $publishDirectory"
    }

    $files = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum

    $archive = $null
    if (-not $NoArchive) {
        $archive = Join-Path $OutputDirectory "ferrite-$shape-$Runtime.zip"
        if (Test-Path -LiteralPath $archive) {
            Remove-Item -LiteralPath $archive -Force
        }

        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -CompressionLevel Optimal
    }

    $results += [pscustomobject]@{
        Shape    = $shape
        Files    = $files.Count
        Size     = '{0:N1} MiB' -f ($bytes / 1MB)
        Archive  = if ($archive) { '{0:N1} MiB' -f ((Get-Item -LiteralPath $archive).Length / 1MB) } else { '-' }
        Path     = $publishDirectory
    }
}

Write-Output ''
Write-Output 'Packaging complete. Ferrite is an original launcher; nothing third-party is bundled.'
$results | Format-Table -AutoSize | Out-String | Write-Output

foreach ($result in $results) {
    Write-Output ("{0,-20} {1,6} file(s)  {2,10}  archive {3}" -f
        $result.Shape, $result.Files, $result.Size, $result.Archive)
}

Write-Output ''
Write-Output 'Next: run the executable with FERRITE_HOME pointing at a data directory to try it.'
