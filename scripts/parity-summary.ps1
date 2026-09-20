$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$parityFile = Join-Path $root 'FEATURE_PARITY.md'

if (-not (Test-Path -LiteralPath $parityFile)) {
    throw "FEATURE_PARITY.md not found at $parityFile"
}

$statuses = 'NOT STARTED', 'IN PROGRESS', 'IMPLEMENTED', 'VERIFIED', 'BLOCKED EXTERNAL'
$counts = @{}
foreach ($status in $statuses) { $counts[$status] = 0 }

foreach ($line in Get-Content -LiteralPath $parityFile) {
    if ($line -notmatch '^\|\s*[A-P]\d{2}\s*\|') { continue }
    $cells = $line -split '\|'
    if ($cells.Count -lt 6) { continue }
    $status = $cells[4].Trim()
    if ($counts.ContainsKey($status)) {
        $counts[$status]++
    } else {
        Write-Warning "Row has an unrecognised status '$status'"
    }
}

$total = 0
foreach ($status in $statuses) { $total += $counts[$status] }

Write-Output "Parity rows: $total"
foreach ($status in $statuses) {
    Write-Output ("  {0,-16} {1}" -f $status, $counts[$status])
}

$content = Get-Content -LiteralPath $parityFile -Raw
$summary = @('| Status | Count |', '| --- | --- |')
foreach ($status in $statuses) {
    $summary += "| $status | $($counts[$status]) |"
}
$summaryText = $summary -join "`r`n"

$pattern = '(?s)(## Summary\r?\n\r?\n)(\| Status \| Count \|.*?)(\r?\n)'
if ($content -notmatch $pattern) {
    throw 'Could not find the Summary table in FEATURE_PARITY.md'
}

$updated = [regex]::Replace($content, $pattern, { param($m) $m.Groups[1].Value + $summaryText + $m.Groups[3].Value })
if ($updated -ne $content) {
    Set-Content -LiteralPath $parityFile -Value $updated -NoNewline
    Write-Output 'Summary table updated.'
} else {
    Write-Output 'Summary table already correct.'
}
