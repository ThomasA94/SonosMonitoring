[CmdletBinding()]
param([ValidatePattern('^[1-9][0-9]*[sm]$')][string]$Timeout = '15m')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$reportDirectory = Join-Path $repositoryRoot 'artifacts/security'
New-Item -ItemType Directory -Force $reportDirectory | Out-Null
# Trivy 0.74.0; scan exported images without granting access to the Docker socket.
$scanner = 'aquasec/trivy@sha256:62b1e65e8869bc4b4c6aa4fa2b21595256c7c2f6018a9d9ad61caf87187c1969'
$findings = 0
foreach ($hostName in @('api', 'projections', 'web')) {
    $archive = Join-Path $reportDirectory "$hostName.tar"
    try {
        & docker save -o $archive "sonosmonitoring-${hostName}:local"
        if ($LASTEXITCODE -ne 0) { throw "Image export failed for $hostName." }
        & docker run --rm --mount "type=bind,source=$reportDirectory,target=/scan" $scanner image --quiet --timeout $Timeout --input "/scan/$hostName.tar" --cache-dir /scan/cache --scanners vuln --severity HIGH,CRITICAL --format json --output "/scan/$hostName-vulnerabilities.json"
        if ($LASTEXITCODE -ne 0) { throw "Vulnerability scan failed for $hostName." }
        $report = Get-Content -LiteralPath (Join-Path $reportDirectory "$hostName-vulnerabilities.json") -Raw | ConvertFrom-Json
        $count = @($report.Results | ForEach-Object { $_.Vulnerabilities } | Where-Object { $null -ne $_ }).Count
        $findings += $count
        Write-Host "$hostName image: $count HIGH/CRITICAL findings."
    }
    finally {
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    }
}
if ($findings -gt 0) { throw "$findings HIGH/CRITICAL findings require review; see artifacts/security/*.json." }
