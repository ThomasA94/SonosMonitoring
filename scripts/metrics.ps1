[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Up', 'Down', 'Restart')]
    [string]$Action = 'Up'
)

$ErrorActionPreference = 'Stop'
$projectName = 'sonosmonitoring-metrics'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'infra/compose.metrics.yaml'
$generatedDirectory = Join-Path $repositoryRoot '.local/metrics'

function Invoke-MetricsCompose {
    <# Runs Compose only for the dedicated optional metrics project. #>
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & docker compose --project-name $projectName --file $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose failed with exit code $LASTEXITCODE."
    }
}

function Assert-RequiredEnvironmentVariable {
    <# Requires a non-empty process environment variable without showing its value. #>
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Set $Name in the process environment before starting the metrics stack."
    }

    return $value
}

function New-PrometheusConfiguration {
    <# Writes a scrape configuration and a separate API-key file under the ignored local directory. #>
    param(
        [Parameter(Mandatory)]
        [string]$ApiKey,

        [Parameter(Mandatory)]
        [string]$Target
    )

    New-Item -ItemType Directory -Path $generatedDirectory -Force | Out-Null
    $template = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot 'infra/prometheus/prometheus.yml.template'))
    $configuration = $template.Replace('__SCRAPE_TARGET__', $Target)
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Join-Path $generatedDirectory 'prometheus.yml'), $configuration, $utf8WithoutBom)
    [System.IO.File]::WriteAllText((Join-Path $generatedDirectory 'api-key'), $ApiKey, $utf8WithoutBom)
}

function ConvertTo-PrometheusDurationMilliseconds {
    <# Converts a single Prometheus duration unit to milliseconds for normalized-value comparison. #>
    param(
        [Parameter(Mandatory)]
        [string]$Duration
    )

    $durationMatch = [regex]::Match($Duration, '^(?<value>[0-9]+)(?<unit>ms|y|w|d|h|m|s)$')
    if ($durationMatch.Success -eq $false) {
        return $null
    }

    [long]$unitMilliseconds = switch ($durationMatch.Groups['unit'].Value) {
        'ms' { 1 }
        's' { 1000 }
        'm' { 60000 }
        'h' { 3600000 }
        'd' { 86400000 }
        'w' { 604800000 }
        'y' { 31536000000 }
    }
    return [long]$durationMatch.Groups['value'].Value * $unitMilliseconds
}

function Wait-ForPrometheus {
    <# Waits for the isolated Prometheus HTTP API and verifies its effective retention flag. #>
    param(
        [Parameter(Mandatory)]
        [string]$ExpectedRetention
    )

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    $lastException = $null
    $expectedMilliseconds = ConvertTo-PrometheusDurationMilliseconds -Duration $ExpectedRetention
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-RestMethod -Uri 'http://127.0.0.1:48090/api/v1/status/flags' -TimeoutSec 3
            $actualRetention = [string]$response.data.'storage.tsdb.retention.time'
            $actualMilliseconds = ConvertTo-PrometheusDurationMilliseconds -Duration $actualRetention
            if ($response.status -eq 'success' -and $null -ne $actualMilliseconds -and $actualMilliseconds -eq $expectedMilliseconds) {
                return
            }
        }
        catch {
            $lastException = $_.Exception
        }

        Start-Sleep -Seconds 1
    }

    if ($null -ne $lastException) {
        throw "Prometheus did not become ready within two minutes. $($lastException.Message)"
    }

    throw "Prometheus did not report retention $ExpectedRetention within two minutes."
}

if ((Test-Path -LiteralPath $composeFile) -eq $false) {
    throw "Compose file was not found: $composeFile"
}

if ($Action -eq 'Down') {
    $createdGeneratedDirectory = (Test-Path -LiteralPath $generatedDirectory) -eq $false
    New-Item -ItemType Directory -Path $generatedDirectory -Force | Out-Null
    $prometheusConfigurationPath = Join-Path $generatedDirectory 'prometheus.yml'
    $apiKeyPath = Join-Path $generatedDirectory 'api-key'
    $createdPrometheusConfiguration = (Test-Path -LiteralPath $prometheusConfigurationPath) -eq $false
    $createdApiKeyFile = (Test-Path -LiteralPath $apiKeyPath) -eq $false
    if ($createdPrometheusConfiguration) {
        [System.IO.File]::WriteAllText($prometheusConfigurationPath, "global:`n  scrape_interval: 15s`n")
    }
    if ($createdApiKeyFile) {
        [System.IO.File]::WriteAllText($apiKeyPath, '')
    }

    $savedAdminUser = [Environment]::GetEnvironmentVariable('GRAFANA_ADMIN_USER', 'Process')
    $savedAdminPassword = [Environment]::GetEnvironmentVariable('GRAFANA_ADMIN_PASSWORD', 'Process')
    if ([string]::IsNullOrWhiteSpace($savedAdminUser)) {
        [Environment]::SetEnvironmentVariable('GRAFANA_ADMIN_USER', 'compose-down-only', 'Process')
    }
    if ([string]::IsNullOrWhiteSpace($savedAdminPassword)) {
        [Environment]::SetEnvironmentVariable('GRAFANA_ADMIN_PASSWORD', 'compose-down-only', 'Process')
    }

    try {
        Invoke-MetricsCompose -Arguments @('down', '--remove-orphans')
    }
    finally {
        [Environment]::SetEnvironmentVariable('GRAFANA_ADMIN_USER', $savedAdminUser, 'Process')
        [Environment]::SetEnvironmentVariable('GRAFANA_ADMIN_PASSWORD', $savedAdminPassword, 'Process')
    }

    Remove-Item -LiteralPath $apiKeyPath, $prometheusConfigurationPath -Force -ErrorAction SilentlyContinue
    if ($createdGeneratedDirectory -and (Get-ChildItem -LiteralPath $generatedDirectory -Force | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $generatedDirectory -Force
    }
    return
}

$apiKey = Assert-RequiredEnvironmentVariable -Name 'SONOSMONITORING_API_KEY'
$grafanaUser = Assert-RequiredEnvironmentVariable -Name 'GRAFANA_ADMIN_USER'
$grafanaPassword = Assert-RequiredEnvironmentVariable -Name 'GRAFANA_ADMIN_PASSWORD'
$target = [Environment]::GetEnvironmentVariable('SONOSMONITORING_METRICS_TARGET', 'Process')
if ([string]::IsNullOrWhiteSpace($target)) {
    $target = 'host.docker.internal:48080'
}
if ($target -notmatch '^[A-Za-z0-9.-]+:[0-9]{1,5}$' -or [int]($target.Split(':')[-1]) -gt 65535) {
    throw 'SONOSMONITORING_METRICS_TARGET must be a host:port value.'
}

$retention = [Environment]::GetEnvironmentVariable('PROMETHEUS_RETENTION', 'Process')
if ([string]::IsNullOrWhiteSpace($retention)) {
    $retention = '400d'
}
if ($retention -notmatch '^[0-9]+(ms|y|w|d|h|m|s)$') {
    throw 'PROMETHEUS_RETENTION must be a Prometheus duration such as 7d or 400d.'
}

[Environment]::SetEnvironmentVariable('PROMETHEUS_RETENTION', $retention, 'Process')
[Environment]::SetEnvironmentVariable('GRAFANA_ADMIN_USER', $grafanaUser, 'Process')
[Environment]::SetEnvironmentVariable('GRAFANA_ADMIN_PASSWORD', $grafanaPassword, 'Process')
New-PrometheusConfiguration -ApiKey $apiKey -Target $target

if ($Action -eq 'Restart') {
    # Recreate to apply changed bind mounts and flags while preserving the named data volumes.
    Invoke-MetricsCompose -Arguments @('up', '--detach', '--force-recreate')
}
else {
    Invoke-MetricsCompose -Arguments @('up', '--detach')
}

Wait-ForPrometheus -ExpectedRetention $retention
Write-Output "Metrics services are ready with Prometheus retention $retention."
Write-Output 'Prometheus: http://localhost:48090'
Write-Output 'Grafana:    http://localhost:48300 (configured admin login required)'
