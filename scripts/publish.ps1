[CmdletBinding()]
param([switch]$BuildImages)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
foreach ($hostName in @('Api', 'Projections', 'Web')) {
    $output = "artifacts/publish/$($hostName.ToLowerInvariant())"
    & dotnet publish "src/SonosMonitoring.$hostName" -c Release --no-restore -o $output
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $hostName." }
    if ($BuildImages) {
        & docker build -f "infra/Dockerfile.$($hostName.ToLowerInvariant())" -t "sonosmonitoring-$($hostName.ToLowerInvariant()):local" .
        if ($LASTEXITCODE -ne 0) { throw "Image build failed for $hostName." }
    }
}
