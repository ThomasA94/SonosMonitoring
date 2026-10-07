[CmdletBinding()]
param([switch]$IncludeInfrastructure)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

Invoke-DotNet @('restore', 'SonosMonitoring.slnx', '--locked-mode')
Invoke-DotNet @('build', 'SonosMonitoring.slnx', '-c', 'Release', '--no-restore', '--no-incremental')
Invoke-DotNet @('format', 'SonosMonitoring.slnx', '--no-restore', '--verify-no-changes')
$api = 'src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll'
New-Item -ItemType Directory -Force 'artifacts/catalogs' | Out-Null
Invoke-DotNet @($api, '--devbone-event-contracts-verify', 'contracts/monitoring.events.json')
Invoke-DotNet @($api, '--devbone-event-contracts', 'artifacts/catalogs/monitoring.events.json')
Invoke-DotNet @($api, '--write-metric-catalog', 'artifacts/catalogs/metrics.json')
foreach ($name in @('monitoring.events.json', 'metrics.json')) {
    $baseline = [System.IO.File]::ReadAllText((Join-Path (Get-Location) "contracts/$name")).Replace("`r`n", "`n").Trim()
    $generated = [System.IO.File]::ReadAllText((Join-Path (Get-Location) "artifacts/catalogs/$name")).Replace("`r`n", "`n").Trim()
    if ($baseline -cne $generated) { throw "Catalogue $name is stale; review the generated change before accepting a new baseline." }
}
& "$PSScriptRoot/test-grafana-dashboards.ps1"
Invoke-DotNet @('test', 'tests/SonosMonitoring.Unit.Tests', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=unit.trx', '--results-directory', 'artifacts/test-results')
Invoke-DotNet @('test', 'tests/SonosMonitoring.Web.Tests', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=web.trx', '--results-directory', 'artifacts/test-results')
if ($IncludeInfrastructure) {
    & "$PSScriptRoot/test-alerts.ps1"
    & "$PSScriptRoot/test-backup.ps1"
    & "$PSScriptRoot/test-prometheus-backup.ps1"
    $previousNativeArgumentPassing = $PSNativeCommandArgumentPassing
    try {
        # Regression coverage for JavaScript quotes lost by legacy native argument marshalling.
        $PSNativeCommandArgumentPassing = 'Legacy'
        & "$PSScriptRoot/test-infrastructure.ps1" Up
    }
    finally {
        $PSNativeCommandArgumentPassing = $previousNativeArgumentPassing
    }
    try {
        Invoke-DotNet @('test', 'tests/SonosMonitoring.Integration.Tests', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=integration.trx', '--results-directory', 'artifacts/test-results')
        Invoke-DotNet @('test', 'tests/SonosMonitoring.E2E.Tests', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=e2e.trx', '--results-directory', 'artifacts/test-results')
    }
    finally {
        & "$PSScriptRoot/test-infrastructure.ps1" Down
    }
}
