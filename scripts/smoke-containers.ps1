[CmdletBinding()]
param(
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$projectName = 'sonosmonitoring-tests'
$composeFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'infra/compose.tests.yaml'
$repoRoot = Split-Path -Parent $PSScriptRoot
$networkName = "$projectName`_default"
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$apiContainer = "$projectName-smoke-api-$suffix"
$workerContainer = "$projectName-smoke-worker-$suffix"
$smokeDirectory = Join-Path $repoRoot ".local/smoke-containers/$suffix"
$apiEnvFile = Join-Path $smokeDirectory 'api.env'
$workerConfig = Join-Path $smokeDirectory 'projections.json'
Add-Type -AssemblyName System.Net.Http
$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds(3)
$createdApi = $false
$createdWorker = $false
$mongoDatabase = "sonosmonitoring_smoke_$suffix"
$queuePrefix = "sonosmonitoring-smoke-$suffix"
$domainExchange = "sonosmonitoring.smoke.$suffix.events"

function Invoke-Docker {
    <# Runs Docker and stops when the command fails. #>
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed: docker $($Arguments[0]) (exit code $LASTEXITCODE)."
    }

    return $output
}

function Get-FreeLoopbackPort {
    <# Reserves an available ephemeral port briefly to choose a host mapping. #>
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Get-HttpStatusCode {
    <# Returns a response status without logging request headers or response bodies. #>
    param(
        [Parameter(Mandatory)]
        [string]$Uri,

        [string]$ApiKey
    )

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Uri)
    if ([string]::IsNullOrEmpty($ApiKey) -eq $false) {
        $request.Headers.Add('X-Api-Key', $ApiKey)
    }

    try {
        $response = $httpClient.SendAsync($request).GetAwaiter().GetResult()
        try {
            return [int]$response.StatusCode
        }
        finally {
            $response.Dispose()
        }
    }
    catch {
        return 0
    }
    finally {
        $request.Dispose()
    }
}

function Wait-ForHttpStatus {
    <# Polls a local container health endpoint until it returns the expected status. #>
    param(
        [Parameter(Mandatory)]
        [string]$Uri,

        [Parameter(Mandatory)]
        [int]$ExpectedStatus,

        [Parameter(Mandatory)]
        [int]$Timeout,

        [string]$ApiKey
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ((Get-HttpStatusCode -Uri $Uri -ApiKey $ApiKey) -eq $ExpectedStatus) {
            return
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Health endpoint did not return HTTP $ExpectedStatus within $Timeout seconds."
}

function Get-RabbitQueues {
    <# Reads declared test-broker queues to prove that the projection pipeline connected. #>
    $output = Invoke-Docker -Arguments @(
        'compose', '--project-name', $projectName, '--file', $composeFile,
        'exec', '-T', 'rabbitmq', 'rabbitmqctl', 'list_queues', 'name', 'consumers'
    )
    return @($output | Select-Object -Skip 1)
}

function Get-ContainerUser {
    <# Reads configured image user metadata for a named smoke container. #>
    param(
        [Parameter(Mandatory)]
        [string]$ContainerName
    )

    $output = Invoke-Docker -Arguments @('inspect', '--format', '{{.Config.User}}', $ContainerName)
    return (($output | Select-Object -First 1).Trim())
}

try {
    if ((Test-Path -LiteralPath $composeFile) -eq $false) {
        throw "Test Compose file was not found: $composeFile"
    }

    $testServices = Invoke-Docker -Arguments @(
        'compose', '--project-name', $projectName, '--file', $composeFile, 'ps', '--status', 'running', '--services'
    )
    foreach ($service in @('mongo', 'rabbitmq')) {
        if ($testServices -notcontains $service) {
            throw "The shared test service '$service' must be running before smoke testing."
        }
    }

    $network = Invoke-Docker -Arguments @('network', 'inspect', $networkName, '--format', '{{.Name}}')
    if (($network | Select-Object -First 1).Trim() -ne $networkName) {
        throw "The expected shared test network '$networkName' was not found."
    }

    foreach ($image in @('sonosmonitoring-api:local', 'sonosmonitoring-projections:local')) {
        $imageInfo = Invoke-Docker -Arguments @('image', 'inspect', '--format', '{{.Os}}/{{.Architecture}}', $image)
        if (($imageInfo | Select-Object -First 1).Trim() -ne 'linux/amd64') {
            throw "Expected the built Linux image '$image' to be available."
        }
    }

    New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null
    $apiKeyBytes = [byte[]]::new(48)
    $randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $randomNumberGenerator.GetBytes($apiKeyBytes)
    $randomNumberGenerator.Dispose()
    $apiKey = [Convert]::ToBase64String($apiKeyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    [System.IO.File]::WriteAllText($apiEnvFile, "Access__ApiKey=$apiKey`nBackend__Enabled=false`nurls=http://0.0.0.0:8080`n")

    $apiPort = Get-FreeLoopbackPort
    [void](Invoke-Docker -Arguments @(
        'run', '--detach', '--name', $apiContainer, '--network', $networkName,
        '--publish', "127.0.0.1:${apiPort}:8080", '--env-file', $apiEnvFile,
        'sonosmonitoring-api:local'
    ))
    $createdApi = $true

    $apiUri = "http://127.0.0.1:$apiPort/health/ready"
    Wait-ForHttpStatus -Uri $apiUri -ExpectedStatus 200 -Timeout $TimeoutSeconds -ApiKey $apiKey
    if ((Get-HttpStatusCode -Uri $apiUri) -ne 403) {
        throw 'The API did not reject an unauthenticated readiness request with HTTP 403.'
    }

    $apiUser = Get-ContainerUser -ContainerName $apiContainer
    if ($apiUser -ne 'app') {
        throw "The API image configured user '$apiUser' instead of 'app'."
    }

    $sdkOutput = Invoke-Docker -Arguments @('exec', $apiContainer, 'dotnet', '--list-sdks')
    if (@($sdkOutput | Where-Object { [string]::IsNullOrWhiteSpace($_) -eq $false }).Count -gt 0) {
        throw 'The API runtime image unexpectedly contains a .NET SDK.'
    }

    $projectionSettings = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/projections.example.json') -Raw | ConvertFrom-Json
    $projectionSettings.EventProjection.Mongo.ConnectionString = 'mongodb://mongo:27017/?replicaSet=rs0&directConnection=true'
    $projectionSettings.EventProjection.Mongo.DatabaseName = $mongoDatabase
    $projectionSettings.EventProjection.ControlPlane.DeploymentId = "sonosmonitoring-smoke-$suffix"
    $projectionSettings.EventProjection.ControlPlane.Revision = 'container-smoke'
    $projectionSettings.EventProjection.InternalAddressPrefix = "sonosmonitoring.smoke.$suffix"
    $projectionSettings.EventProjection.RabbitMq.HostName = 'rabbitmq'
    $projectionSettings.EventProjection.RabbitMq.Port = 5672
    $projectionSettings.EventProjection.RabbitMq.UserName = 'sonosmonitoring_test'
    $projectionSettings.EventProjection.RabbitMq.Password = 'sonosmonitoring_test_password'
    $projectionSettings.EventProjection.RabbitMq.DomainExchangeName = $domainExchange
    $projectionSettings.EventProjection.RabbitMq.QueuePrefix = $queuePrefix
    $projectionSettings | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $workerConfig -Encoding utf8

    $workerPort = Get-FreeLoopbackPort
    [void](Invoke-Docker -Arguments @(
        'run', '--detach', '--name', $workerContainer, '--network', $networkName,
        '--publish', "127.0.0.1:${workerPort}:8080",
        '--mount', "type=bind,source=$workerConfig,target=/run-config/projections.json,readonly",
        '--env', 'SONOSMONITORING_CONFIG=/run-config/projections.json',
        'sonosmonitoring-projections:local'
    ))
    $createdWorker = $true

    Wait-ForHttpStatus -Uri "http://127.0.0.1:$workerPort/health/live" -ExpectedStatus 200 -Timeout $TimeoutSeconds

    $queueDeadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $workerQueueFound = $false
    while ([DateTimeOffset]::UtcNow -lt $queueDeadline) {
        $queues = Get-RabbitQueues
        if (@($queues | Where-Object { $_ -match [regex]::Escape($queuePrefix) }).Count -gt 0) {
            $workerQueueFound = $true
            break
        }

        Start-Sleep -Seconds 1
    }

    if ($workerQueueFound -eq $false) {
        throw 'The projection worker health endpoint was live, but its isolated RabbitMQ queue was not created.'
    }

    $workerUser = Get-ContainerUser -ContainerName $workerContainer
    if ($workerUser -ne 'app') {
        throw "The projection image configured user '$workerUser' instead of 'app'."
    }

    $workerSdkOutput = Invoke-Docker -Arguments @('exec', $workerContainer, 'dotnet', '--list-sdks')
    if (@($workerSdkOutput | Where-Object { [string]::IsNullOrWhiteSpace($_) -eq $false }).Count -gt 0) {
        throw 'The projection runtime image unexpectedly contains a .NET SDK.'
    }

    $workerLogs = Invoke-Docker -Arguments @('logs', '--tail', '200', $workerContainer)
    if (@($workerLogs | Where-Object { $_ -match 'fail|exception|error' }).Count -gt 0) {
        throw 'The projection worker logs contain startup errors; inspect the disposable container before cleanup.'
    }

    Write-Output 'API image: authenticated readiness returned 200; unauthenticated readiness returned 403.'
    Write-Output 'API image: configured user is app; runtime image reports no installed .NET SDK.'
    Write-Output 'Projection image: liveness returned 200 and its isolated RabbitMQ queue was declared.'
    Write-Output 'Projection logs: no startup errors detected.'
}
finally {
    if ($createdWorker) {
        & docker rm --force $workerContainer *> $null
    }
    if ($createdApi) {
        & docker rm --force $apiContainer *> $null
    }

    if ($createdWorker) {
        $dropSmokeDatabase = "const result = db.getSiblingDB('$mongoDatabase').dropDatabase(); quit(result.ok === 1 ? 0 : 1);"
        & docker compose --project-name $projectName --file $composeFile exec -T mongo mongosh --quiet --eval $dropSmokeDatabase *> $null
        # RabbitMQ queues and exchanges have run-specific names and are discarded by test-infrastructure.ps1 Down.
    }

    $httpClient.Dispose()

    if ((Test-Path -LiteralPath $smokeDirectory) -and (Split-Path -Parent $smokeDirectory) -eq (Join-Path $repoRoot '.local/smoke-containers')) {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}
