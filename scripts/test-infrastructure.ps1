[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Up', 'Down')]
    [string]$Action = 'Up',

    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$projectName = 'sonosmonitoring-tests'
$composeFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'infra/compose.tests.yaml'

if ((Test-Path -LiteralPath $composeFile) -eq $false) {
    throw "Compose file was not found: $composeFile"
}

function Invoke-TestCompose {
    <# Runs Compose only for the dedicated integration-test project. #>
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & docker compose --project-name $projectName --file $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose failed with exit code $LASTEXITCODE."
    }
}

function Test-CommandSucceeds {
    <# Returns whether a command completed successfully. #>
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments *> $null
    return $LASTEXITCODE -eq 0
}

function Test-RabbitMqPort {
    <# The host port proxy may accept before the broker; inspect its listener inside the container. #>
    return Test-CommandSucceeds -FilePath 'docker' -Arguments @(
        'compose', '--project-name', $projectName, '--file', $composeFile,
        'exec', '-T', 'rabbitmq', 'bash', '-c', 'exec 3<>/dev/tcp/127.0.0.1/5672'
    )
}

function Wait-ForTestServices {
    <# Waits for PostgreSQL, RabbitMQ, and MongoDB, then initializes MongoDB's replica set. #>
    param(
        [Parameter(Mandatory)]
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    $postgresReady = $false
    $rabbitMqReady = $false
    $mongoReady = $false

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($postgresReady -eq $false) {
            $postgresReady = Test-CommandSucceeds -FilePath 'docker' -Arguments @(
                'compose', '--project-name', $projectName, '--file', $composeFile,
                'exec', '-T', 'postgres', 'pg_isready', '-U', 'sonosmonitoring_test', '-d', 'sonosmonitoring_tests'
            )
        }

        if ($rabbitMqReady -eq $false) {
            $rabbitMqReady = Test-RabbitMqPort
        }

        if ($mongoReady -eq $false) {
            $mongoReady = Test-CommandSucceeds -FilePath 'docker' -Arguments @(
                'compose', '--project-name', $projectName, '--file', $composeFile,
                'exec', '-T', 'mongo', 'mongosh', '--quiet', '--eval', 'quit(db.adminCommand({ ping: 1 }).ok === 1 ? 0 : 1)'
            )
        }

        if ($postgresReady -and $rabbitMqReady -and $mongoReady) {
            break
        }

        Start-Sleep -Seconds 1
    }

    if ($postgresReady -eq $false -or $rabbitMqReady -eq $false -or $mongoReady -eq $false) {
        throw "The integration services did not become ready within $Timeout seconds."
    }

    $initializeReplicaSet = @'
try {
  rs.status();
  quit(0);
} catch (error) {
  if (error.codeName !== "NotYetInitialized") {
    print(error);
    quit(1);
  }
}
const result = rs.initiate({ _id: "rs0", members: [{ _id: 0, host: "mongo:27017" }] });
if (result.ok !== 1) {
  printjson(result);
  quit(1);
}
'@
    # Keep JavaScript quotes out of native argument marshalling, including Legacy mode.
    $initializeReplicaSet | & docker compose --project-name $projectName --file $composeFile exec -T mongo mongosh --quiet --file /dev/stdin
    if ($LASTEXITCODE -ne 0) {
        throw "MongoDB test replica-set initialization failed with exit code $LASTEXITCODE."
    }

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $replicaSetReady = Test-CommandSucceeds -FilePath 'docker' -Arguments @(
            'compose', '--project-name', $projectName, '--file', $composeFile,
            'exec', '-T', 'mongo', 'mongosh', '--quiet', '--eval', 'quit(db.hello().isWritablePrimary === true ? 0 : 1)'
        )
        if ($replicaSetReady) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "MongoDB replica set rs0 did not become primary within $Timeout seconds."
}

if ($Action -eq 'Down') {
    Invoke-TestCompose -Arguments @('down', '--remove-orphans', '--volumes')
    return
}

Invoke-TestCompose -Arguments @('up', '--detach')
Wait-ForTestServices -Timeout $TimeoutSeconds

Write-Output 'Integration services are ready.'
Write-Output 'PostgreSQL: Host=localhost;Port=48433;Database=<fixture database>;Username=sonosmonitoring_test;Password=sonosmonitoring_test_password'
Write-Output 'RabbitMQ:   amqp://sonosmonitoring_test:sonosmonitoring_test_password@localhost:48673/%2f'
Write-Output 'MongoDB:    mongodb://localhost:48018/?replicaSet=rs0&directConnection=true'
