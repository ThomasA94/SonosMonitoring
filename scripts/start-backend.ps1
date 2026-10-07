[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Up', 'Api', 'Projections')]
    [string]$Action = 'Up',

    [ValidateRange(15, 600)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [version]::new(7, 3)) {
    & "$PSScriptRoot/invoke-powershell7.ps1" -ScriptPath $PSCommandPath -ScriptArguments @('-Action', $Action, '-TimeoutSeconds', [string]$TimeoutSeconds)
    return
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'infra/compose.yaml'
$localDirectory = Join-Path $repositoryRoot '.local'
$environmentFile = Join-Path $localDirectory 'dev.env'
$backendExample = Join-Path $repositoryRoot 'infra/backend.example.json'
$projectionsExample = Join-Path $repositoryRoot 'infra/projections.example.json'
$backendConfiguration = Join-Path $localDirectory 'backend.json'
$projectionsConfiguration = Join-Path $localDirectory 'projections.json'
$schemaFile = Join-Path $localDirectory 'event-store-schema.sql'
$mediaSchemaFile = Join-Path $localDirectory 'media-control-schema.sql'
$composeProject = 'sonosmonitoring-dev'
$postgresUser = 'sonosmonitoring_dev'
$databaseName = 'sonosmonitoring_dev'

if ((Test-Path -LiteralPath $composeFile) -eq $false) {
    throw "Compose file was not found: $composeFile"
}

function New-LocalSecret {
    <# Generates a URL-safe random secret suitable for the local environment file. #>
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Test-PersistentCredentialsVolume {
    <# Checks whether this stack has storage that may already be bound to older credentials. #>
    foreach ($volumeName in @('sonosmonitoring-dev-postgres-data', 'sonosmonitoring-dev-rabbitmq-data')) {
        & docker volume inspect $volumeName *> $null
        if ($LASTEXITCODE -eq 0) {
            return $true
        }
    }

    return $false
}

function Get-LocalEnvironment {
    <# Creates or reads local credentials without printing their values. #>
    if ((Test-Path -LiteralPath $environmentFile) -eq $false) {
        if (Test-PersistentCredentialsVolume) {
            throw 'The local credential file is missing while persistent database or broker storage exists. Recover the previous .local/dev.env file before continuing.'
        }

        New-Item -ItemType Directory -Path $localDirectory -Force | Out-Null
        $postgresPassword = New-LocalSecret
        $rabbitMqPassword = New-LocalSecret
        @(
            "SONOSMONITORING_DEV_POSTGRES_PASSWORD=$postgresPassword"
            "SONOSMONITORING_DEV_RABBITMQ_PASSWORD=$rabbitMqPassword"
            'SONOSMONITORING_DEV_RABBITMQ_HOSTNAME=sonosmonitoring-dev-rabbitmq'
        ) | Set-Content -LiteralPath $environmentFile -Encoding ascii
    }

    $values = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath $environmentFile) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $name = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim()
        if ($name.Length -gt 0) {
            $values[$name] = $value
        }
    }

    foreach ($name in @('SONOSMONITORING_DEV_POSTGRES_PASSWORD', 'SONOSMONITORING_DEV_RABBITMQ_PASSWORD')) {
        if ($values.ContainsKey($name) -eq $false -or [string]::IsNullOrWhiteSpace($values[$name])) {
            throw "The local environment file must define $name. Recover the previous value before continuing; replacing credentials while persistent volumes exist can make stored data inaccessible."
        }
    }

    if ($values.ContainsKey('SONOSMONITORING_DEV_RABBITMQ_HOSTNAME') -eq $false) {
        # Preserve the node name before Compose replaces a container with existing broker storage.
        $existingContainers = @(& docker ps --all --quiet --filter "label=com.docker.compose.project=$composeProject" --filter 'label=com.docker.compose.service=rabbitmq')
        if ($LASTEXITCODE -ne 0 -or $existingContainers.Count -gt 1) {
            throw 'Could not determine the existing RabbitMQ container identity.'
        }
        if ($existingContainers.Count -eq 1) {
            $brokerHostname = & docker inspect --format '{{.Config.Hostname}}' $existingContainers[0]
            if ($LASTEXITCODE -ne 0) { throw 'Could not read the existing RabbitMQ hostname.' }
        }
        else {
            & docker volume inspect 'sonosmonitoring-dev-rabbitmq-data' *> $null
            if ($LASTEXITCODE -eq 0) {
                throw 'RabbitMQ storage exists without its previous container. Recover SONOSMONITORING_DEV_RABBITMQ_HOSTNAME in .local/dev.env before starting; do not invent a new node identity.'
            }
            $brokerHostname = 'sonosmonitoring-dev-rabbitmq'
        }
        if ($brokerHostname -cnotmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$') {
            throw 'The previous RabbitMQ hostname is not valid for the local environment file.'
        }
        [System.IO.File]::AppendAllText($environmentFile, "`nSONOSMONITORING_DEV_RABBITMQ_HOSTNAME=$brokerHostname`n")
        $values['SONOSMONITORING_DEV_RABBITMQ_HOSTNAME'] = $brokerHostname
    }
    if ($values['SONOSMONITORING_DEV_RABBITMQ_HOSTNAME'] -cnotmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$') {
        throw 'The local environment file must contain a valid SONOSMONITORING_DEV_RABBITMQ_HOSTNAME.'
    }

    return $values
}

function Invoke-DevCompose {
    <# Runs Compose with this task's separate project name, environment file, and stack. #>
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose failed with exit code $LASTEXITCODE."
    }
}

function Test-DevComposeCommand {
    <# Runs one quiet container readiness command and returns its exit status. #>
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile @Arguments *> $null
    return $LASTEXITCODE -eq 0
}

function Wait-ForDevServices {
    <# Waits for local backing services to accept connections. #>
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
            $postgresReady = Test-DevComposeCommand -Arguments @(
                'exec', '-T', 'postgres', 'pg_isready', '-U', $postgresUser, '-d', $databaseName
            )
        }

        if ($rabbitMqReady -eq $false) {
            $rabbitMqReady = Test-DevComposeCommand -Arguments @(
                'exec', '-T', 'rabbitmq', 'bash', '-c', 'exec 3<>/dev/tcp/127.0.0.1/5672'
            )
        }

        if ($mongoReady -eq $false) {
            $mongoReady = Test-DevComposeCommand -Arguments @(
                'exec', '-T', 'mongo', 'mongosh', '--quiet', '--eval',
                'quit(db.adminCommand({ ping: 1 }).ok === 1 ? 0 : 1)'
            )
        }

        if ($postgresReady -and $rabbitMqReady -and $mongoReady) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "The local services did not become ready within $Timeout seconds."
}

function Initialize-MongoReplicaSet {
    <# Initializes the one-node replica set only when it has not been initialized. #>
    $initializeScript = @'
try {
  const status = rs.status();
  if (status.ok === 1 && status.set === "rs0") {
    quit(0);
  }
  print("MongoDB is already initialized with a different replica set.");
  quit(1);
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
    $initializeScript | & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T mongo mongosh --quiet --file /dev/stdin
    if ($LASTEXITCODE -ne 0) {
        throw "MongoDB replica-set initialization failed with exit code $LASTEXITCODE."
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-DevComposeCommand -Arguments @(
                'exec', '-T', 'mongo', 'mongosh', '--quiet', '--eval',
                'quit(db.hello().isWritablePrimary === true ? 0 : 1)'
            )) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "MongoDB replica set rs0 did not become primary within $TimeoutSeconds seconds."
}

function Initialize-EventStoreSchema {
    <# Creates schema only for a database with no schema marker and no public tables. #>
    $schemaExists = & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -U $postgresUser -d $databaseName -tA -c "SELECT to_regclass('public.event_store_schema') IS NOT NULL"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the local event-store schema.'
    }

    if (($schemaExists | Out-String).Trim() -eq 't') {
        Write-Output 'Event-store schema already exists; keeping it unchanged.'
        return
    }

    $publicTableCount = & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -U $postgresUser -d $databaseName -tA -c "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect public tables before event-store schema creation.'
    }

    $tableCount = [int](($publicTableCount | Out-String).Trim())
    if ($tableCount -ne 0) {
        throw "Event-store schema is absent, but the database already contains $tableCount public table(s). Refusing to initialize over existing data."
    }

    $apiAssembly = Join-Path $repositoryRoot 'src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll'
    if ((Test-Path -LiteralPath $apiAssembly) -eq $false) {
        throw "Release API assembly not found. Build it first: dotnet build SonosMonitoring.slnx -c Release"
    }

    & dotnet $apiAssembly '--write-event-store-schema' $schemaFile
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $schemaFile) -eq $false) {
        throw 'The API schema command did not produce the event-store schema file.'
    }

    $schemaSql = Get-Content -LiteralPath $schemaFile -Raw
    $schemaSql | & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -v ON_ERROR_STOP=1 -U $postgresUser -d $databaseName -f -
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL rejected the generated event-store schema.'
    }
}

function Initialize-MediaControlSchema {
    <# Installs additive media coordination only into a missing namespace; validates existing version 1. #>
    $schemaExists = & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -X -U $postgresUser -d $databaseName -tA -v ON_ERROR_STOP=1 -c "SELECT to_regnamespace('sonos_media') IS NOT NULL"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the local media-control namespace.' }

    if (($schemaExists | Out-String).Trim() -eq 't') {
        $tables = & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -X -U $postgresUser -d $databaseName -tA -v ON_ERROR_STOP=1 -c "SELECT string_agg(tablename, ',' ORDER BY tablename) FROM pg_tables WHERE schemaname='sonos_media'"
        if ($LASTEXITCODE -ne 0 -or ($tables | Out-String).Trim() -cne 'control,decisions,operations,steps,target_epochs,version') {
            throw 'Existing sonos_media namespace has missing or unexpected tables. Refusing to initialize over an incompatible schema.'
        }

        $columnsQuery = @'
SELECT string_agg(table_name || ':' || column_name, ',' ORDER BY table_name, ordinal_position)
FROM information_schema.columns WHERE table_schema='sonos_media';
'@
        $columns = & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -X -U $postgresUser -d $databaseName -tA -v ON_ERROR_STOP=1 -c $columnsQuery
        $expectedColumns = 'control:singleton,control:enabled,control:generation,control:restored_at,' +
            'decisions:id,decisions:fingerprint,decisions:decided_at,' +
            'operations:id,operations:position,operations:fingerprint,operations:generation,operations:targets,operations:payload,' +
            'operations:accepted_at,operations:expires_at,operations:status,operations:step,operations:code,operations:risk_released,operations:outcome_recorded,' +
            'steps:operation,steps:step,steps:released_at,target_epochs:device_key,target_epochs:epoch,version:singleton,version:version'
        if ($LASTEXITCODE -ne 0 -or ($columns | Out-String).Trim() -cne $expectedColumns) {
            throw 'Existing sonos_media columns differ from the installed version 1 contract. Refusing to continue.'
        }

        $version = & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -X -U $postgresUser -d $databaseName -tA -v ON_ERROR_STOP=1 -c 'SELECT version FROM sonos_media.version WHERE singleton'
        if ($LASTEXITCODE -ne 0 -or ($version | Out-String).Trim() -cne '1') {
            throw 'Existing sonos_media version marker is missing or unsupported. Refusing to continue.'
        }
        Write-Output 'Media coordination schema version 1 already exists; keeping it unchanged.'
        return
    }

    if (($schemaExists | Out-String).Trim() -cne 'f') { throw 'Could not determine media-control namespace state.' }
    $apiAssembly = Join-Path $repositoryRoot 'src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll'
    if ((Test-Path -LiteralPath $apiAssembly -PathType Leaf) -eq $false) { throw 'Release API assembly not found. Build it before media schema creation.' }

    & dotnet $apiAssembly '--write-media-control-schema' $mediaSchemaFile
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $mediaSchemaFile -PathType Leaf) -eq $false) {
        throw 'The API schema command did not produce the media-control schema file.'
    }
    Get-Content -LiteralPath $mediaSchemaFile -Raw | & docker compose --project-name $composeProject --env-file $environmentFile --file $composeFile exec -T postgres psql -X -v ON_ERROR_STOP=1 -U $postgresUser -d $databaseName -f -
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL rejected the generated media-control schema.' }
    Write-Output 'Media coordination schema version 1 installed with control disabled.'
}

function Write-LocalConfiguration {
    <# Creates ignored local host settings from examples without replacing user edits. #>
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, string]]$Environment
    )

    New-Item -ItemType Directory -Path $localDirectory -Force | Out-Null

    if ((Test-Path -LiteralPath $backendConfiguration) -eq $false) {
        $backend = Get-Content -LiteralPath $backendExample -Raw | ConvertFrom-Json -AsHashtable
        $backend.EventStore.Password = $Environment['SONOSMONITORING_DEV_POSTGRES_PASSWORD']
        $backend.Backend.RabbitMq.Password = $Environment['SONOSMONITORING_DEV_RABBITMQ_PASSWORD']
        $backendJson = ConvertTo-Json -InputObject $backend -Depth 32
        Set-Content -LiteralPath $backendConfiguration -Value $backendJson -Encoding utf8
    }

    if ((Test-Path -LiteralPath $projectionsConfiguration) -eq $false) {
        $projections = Get-Content -LiteralPath $projectionsExample -Raw | ConvertFrom-Json -AsHashtable
        $projections.EventProjection.RabbitMq.Password = $Environment['SONOSMONITORING_DEV_RABBITMQ_PASSWORD']
        $projectionsJson = ConvertTo-Json -InputObject $projections -Depth 32
        Set-Content -LiteralPath $projectionsConfiguration -Value $projectionsJson -Encoding utf8
    }
}

function Start-LocalInfrastructure {
    <# Starts and prepares the development-only backing services and host configuration. #>
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, string]]$Environment
    )

    Invoke-DevCompose -Arguments @('up', '--detach')
    Wait-ForDevServices -Timeout $TimeoutSeconds
    Initialize-MongoReplicaSet
    Initialize-EventStoreSchema
    Initialize-MediaControlSchema
    Write-LocalConfiguration -Environment $Environment
    Write-Output 'Development services are ready on loopback ports 48432, 48672, and 48017.'
    Write-Output 'Local host settings are stored under .local and are excluded from version control.'
}

function Start-LocalHost {
    <# Runs one built host in the foreground with its generated local configuration. #>
    param(
        [Parameter(Mandatory)]
        [string]$AssemblyPath,

        [Parameter(Mandatory)]
        [string]$ConfigurationPath
    )

    if ((Test-Path -LiteralPath $AssemblyPath) -eq $false) {
        throw "Release host assembly not found. Build it first: dotnet build SonosMonitoring.slnx -c Release"
    }

    if ((Test-Path -LiteralPath $ConfigurationPath) -eq $false) {
        throw "Host configuration was not created at $ConfigurationPath. Run scripts/start-backend.ps1 Up first."
    }

    $previousConfiguration = $env:SONOSMONITORING_CONFIG
    try {
        $env:SONOSMONITORING_CONFIG = $ConfigurationPath
        & dotnet $AssemblyPath
        if ($LASTEXITCODE -ne 0) {
            throw "Host exited with code $LASTEXITCODE."
        }
    }
    finally {
        if ($null -eq $previousConfiguration) {
            Remove-Item Env:SONOSMONITORING_CONFIG -ErrorAction SilentlyContinue
        }
        else {
            $env:SONOSMONITORING_CONFIG = $previousConfiguration
        }
    }
}

if ((Get-Command docker -ErrorAction SilentlyContinue) -eq $null) {
    throw 'Docker Desktop CLI was not found.'
}

& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Desktop is not running or the local engine is unavailable.'
}

$localEnvironment = Get-LocalEnvironment
$previousPostgresPassword = $env:SONOSMONITORING_DEV_POSTGRES_PASSWORD
$previousRabbitMqPassword = $env:SONOSMONITORING_DEV_RABBITMQ_PASSWORD
try {
    $env:SONOSMONITORING_DEV_POSTGRES_PASSWORD = $localEnvironment['SONOSMONITORING_DEV_POSTGRES_PASSWORD']
    $env:SONOSMONITORING_DEV_RABBITMQ_PASSWORD = $localEnvironment['SONOSMONITORING_DEV_RABBITMQ_PASSWORD']
    Start-LocalInfrastructure -Environment $localEnvironment

    if ($Action -eq 'Api') {
        Start-LocalHost -AssemblyPath (Join-Path $repositoryRoot 'src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll') -ConfigurationPath $backendConfiguration
    }
    elseif ($Action -eq 'Projections') {
        Start-LocalHost -AssemblyPath (Join-Path $repositoryRoot 'src/SonosMonitoring.Projections/bin/Release/net10.0/SonosMonitoring.Projections.dll') -ConfigurationPath $projectionsConfiguration
    }
}
finally {
    if ($null -eq $previousPostgresPassword) {
        Remove-Item Env:SONOSMONITORING_DEV_POSTGRES_PASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:SONOSMONITORING_DEV_POSTGRES_PASSWORD = $previousPostgresPassword
    }

    if ($null -eq $previousRabbitMqPassword) {
        Remove-Item Env:SONOSMONITORING_DEV_RABBITMQ_PASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:SONOSMONITORING_DEV_RABBITMQ_PASSWORD = $previousRabbitMqPassword
    }
}
