[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Start', 'Stop', 'Up', 'Down')]
    [string]$Action = 'Start',

    [switch]$SkipBuild,

    # Retained for existing callers; building is now the default.
    [switch]$BuildImages
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [version]::new(7, 3)) {
    $forwardedArguments = @('-Action', $Action)
    if ($SkipBuild) { $forwardedArguments += '-SkipBuild' }
    if ($BuildImages) { $forwardedArguments += '-BuildImages' }
    & "$PSScriptRoot/invoke-powershell7.ps1" -ScriptPath $PSCommandPath -ScriptArguments $forwardedArguments
    return
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
$localDirectory = Join-Path $repositoryRoot '.local/docker'
$credentialsPath = Join-Path $localDirectory 'credentials.json'

function Stop-Stack {
    <# Stops only the two application's Compose projects without requiring generated files or removing data. #>
    foreach ($projectName in @('sonosmonitoring-metrics', 'sonosmonitoring-dev')) {
        $containerIds = @(& docker ps --quiet --filter "label=com.docker.compose.project=$projectName")
        if ($LASTEXITCODE -ne 0) {
            throw "Could not list running containers for $projectName."
        }
        if ($containerIds.Count -gt 0) {
            & docker stop @containerIds
            if ($LASTEXITCODE -ne 0) {
                throw "Could not stop every container for $projectName."
            }
        }
        $remaining = @(& docker ps --quiet --filter "label=com.docker.compose.project=$projectName")
        if ($LASTEXITCODE -ne 0 -or $remaining.Count -gt 0) {
            throw "Could not confirm that $projectName has stopped."
        }
    }
    Write-Output 'Stack stopped. Containers, data and volumes are preserved.'
}

function Write-LocalJson {
    <# Writes runtime configuration outside version control without displaying credentials. #>
    param([string]$Path, [hashtable]$Value)
    [System.IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 30) + "`n")
}

function New-LocalSecret {
    <# Creates a shell-safe, cryptographically random installation credential. #>
    return [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
}

function Wait-ForHttp {
    <# Waits for an HTTP endpoint without exposing request headers on failure. #>
    param([string]$Uri, [hashtable]$Headers = @{})
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $null = Invoke-WebRequest -Uri $Uri -Headers $Headers -TimeoutSec 5
            return
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    throw "Endpoint did not become ready: $Uri. Inspect the corresponding container logs."
}

if ($Action -in @('Stop', 'Down')) {
    Stop-Stack
    return
}
if ($SkipBuild -and $BuildImages) {
    throw 'Use either -SkipBuild or -BuildImages, not both.'
}

& dotnet dev-certs https --check --trust
if ($LASTEXITCODE -ne 0) {
    throw 'A trusted localhost development certificate is required. Run dotnet dev-certs https --trust first.'
}
if ($SkipBuild -eq $false) {
    Write-Output 'Building application images from the current working tree...'
    & dotnet restore SonosMonitoring.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw 'Restore failed. Application containers have not been replaced.'
    }
    & "$PSScriptRoot/publish.ps1" -BuildImages
}
else {
    Write-Output 'Skipping build by request; using existing local application images.'
}
& "$PSScriptRoot/start-backend.ps1" Up

New-Item -ItemType Directory -Path $localDirectory, "$localDirectory/data-protection", '.local/metrics' -Force | Out-Null
if (Test-Path -LiteralPath $credentialsPath) {
    $credentials = Get-Content -LiteralPath $credentialsPath -Raw | ConvertFrom-Json -AsHashtable
}
else {
    $credentials = @{
        ApiKey = New-LocalSecret
        WebAccessKey = New-LocalSecret
        CertificatePassword = New-LocalSecret
        GrafanaBootstrapUser = 'admin'
        GrafanaBootstrapPassword = New-LocalSecret
    }
    if ([string]::IsNullOrWhiteSpace($env:GRAFANA_ADMIN_USER) -eq $false) {
        $credentials.GrafanaBootstrapUser = $env:GRAFANA_ADMIN_USER
    }
    if ([string]::IsNullOrWhiteSpace($env:GRAFANA_ADMIN_PASSWORD) -eq $false) {
        $credentials.GrafanaBootstrapPassword = $env:GRAFANA_ADMIN_PASSWORD
    }
    Write-LocalJson -Path $credentialsPath -Value $credentials
}

$backend = Get-Content '.local/backend.json' -Raw | ConvertFrom-Json -AsHashtable
if ([string]::IsNullOrWhiteSpace($backend.Access.ApiKey) -eq $false) {
    $credentials.ApiKey = $backend.Access.ApiKey
    Write-LocalJson -Path $credentialsPath -Value $credentials
}
$backend.EventStore.Host = 'postgres'
$backend.EventStore.Port = 5432
$backend.Backend.RabbitMq.HostName = 'rabbitmq'
$backend.Backend.RabbitMq.Port = 5672
$backend.Backend.MongoConnectionString = 'mongodb://mongo:27017/?replicaSet=rs0&directConnection=true'
$backend.Access = @{ ApiKey = $credentials.ApiKey }
if ($null -eq $backend.Metrics) { $backend.Metrics = @{} }
$backend.Metrics.PrometheusUrl = 'http://prometheus:9090/'
if ($backend.History.Enabled -eq $true) {
    $historyConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $historyConnection.ConnectionString = $backend.History.ConnectionString
    if ($historyConnection['Host'] -in @('127.0.0.1', 'localhost') -and $historyConnection['Port'] -in @('48432', '55433')) {
        $historyConnection['Host'] = 'postgres'
        $historyConnection['Port'] = '5432'
        $backend.History.ConnectionString = $historyConnection.ConnectionString
    }
}
Write-LocalJson -Path "$localDirectory/backend.json" -Value $backend

$projections = Get-Content '.local/projections.json' -Raw | ConvertFrom-Json -AsHashtable
$projections.EventProjection.Mongo.ConnectionString = $backend.Backend.MongoConnectionString
$projections.EventProjection.RabbitMq.HostName = 'rabbitmq'
$projections.EventProjection.RabbitMq.Port = 5672
Write-LocalJson -Path "$localDirectory/projections.json" -Value $projections

& dotnet dev-certs https --export-path "$localDirectory/localhost.pfx" --password $credentials.CertificatePassword
if ($LASTEXITCODE -ne 0) { throw 'Exporting the local development certificate failed.' }
Write-LocalJson -Path "$localDirectory/web.json" -Value @{
    Api = @{ BaseUrl = 'http://api:8080/'; ApiKey = $credentials.ApiKey }
    Web = @{ AccessKey = $credentials.WebAccessKey }
    Kestrel = @{ Certificates = @{ Default = @{ Path = '/certificates/localhost.pfx'; Password = $credentials.CertificatePassword } } }
}

$template = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot 'infra/prometheus/prometheus.yml.template'))
[System.IO.File]::WriteAllText((Join-Path $repositoryRoot '.local/metrics/prometheus.yml'), $template.Replace('__SCRAPE_TARGET__', 'api:8080'))
[System.IO.File]::WriteAllText((Join-Path $repositoryRoot '.local/metrics/api-key'), $credentials.ApiKey)

# The infrastructure helper already checked dependencies. Recreate only application hosts to reload mounted settings.
& docker compose --project-name sonosmonitoring-dev --env-file .local/dev.env --file infra/compose.yaml --file infra/compose.app.yaml up --detach --no-deps --force-recreate api projections web
if ($LASTEXITCODE -ne 0) { throw 'Starting application containers failed.' }

$savedUser = $env:GRAFANA_ADMIN_USER
$savedPassword = $env:GRAFANA_ADMIN_PASSWORD
try {
    # Grafana reads these only when initializing an empty database. Existing accounts remain unchanged.
    $env:GRAFANA_ADMIN_USER = $credentials.GrafanaBootstrapUser
    $env:GRAFANA_ADMIN_PASSWORD = $credentials.GrafanaBootstrapPassword
    & docker compose --project-name sonosmonitoring-metrics --file infra/compose.metrics.yaml --file infra/compose.metrics.docker.yaml up --detach
    if ($LASTEXITCODE -ne 0) { throw 'Starting metrics containers failed.' }
}
finally {
    $env:GRAFANA_ADMIN_USER = $savedUser
    $env:GRAFANA_ADMIN_PASSWORD = $savedPassword
}

Wait-ForHttp -Uri 'http://127.0.0.1:48080/health/ready' -Headers @{ 'X-Api-Key' = $credentials.ApiKey }
Wait-ForHttp -Uri 'https://localhost:48082/login'
Wait-ForHttp -Uri 'http://127.0.0.1:48090/-/ready'
Wait-ForHttp -Uri 'http://127.0.0.1:48300/api/health'
Write-Output ''
Write-Output 'Stack ready. Service URLs:'
Write-Output '  Frontend:   https://localhost:48082'
Write-Output '  API:        http://localhost:48080 (X-Api-Key required)'
Write-Output '  Grafana:    http://localhost:48300'
Write-Output '  Prometheus: http://localhost:48090'
Write-Output ''
Write-Output 'Frontend login: WebAccessKey in .local/docker/credentials.json (not printed).'
Write-Output 'Existing Grafana accounts are preserved; bootstrap credentials only apply to a new database.'
Write-Output 'Stop: ./scripts/start-stack.ps1 Stop'
