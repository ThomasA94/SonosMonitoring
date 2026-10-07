function Get-PinnedPrometheusImage {
    <# Reads the single Prometheus image reference from the canonical metrics Compose file. #>
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $composeFile = Join-Path $repoRoot 'infra/compose.metrics.yaml'
    if ([System.IO.File]::Exists($composeFile) -eq $false) { throw "Compose file was not found: $composeFile" }
    $matches = Select-String -LiteralPath $composeFile -Pattern '^\s*image:\s*(prom/prometheus:[^\s]+)\s*$'
    if (@($matches).Count -ne 1) { throw 'Expected exactly one pinned Prometheus image in infra/compose.metrics.yaml.' }
    return $matches[0].Matches[0].Groups[1].Value
}
