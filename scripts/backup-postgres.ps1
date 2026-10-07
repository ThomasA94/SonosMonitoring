<#
.SYNOPSIS
Creates a non-overwriting PostgreSQL custom-format backup from a running Docker container.

.DESCRIPTION
Runs pg_dump inside the named container, then transfers the binary archive with docker cp.
PostgreSQL credentials are not accepted on the command line; the container's local auth is used.

.PARAMETER ContainerName
Name of the running PostgreSQL container.

.PARAMETER Database
Source database identifier.

.PARAMETER User
PostgreSQL role used for the local dump connection.

.PARAMETER OutputPath
New backup file path. The output file must not exist; its parent directory must exist.

.EXAMPLE
./scripts/backup-postgres.ps1 -ContainerName sonos-postgres -Database app -User postgres -OutputPath D:\Backups\app.dump
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ContainerName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Database,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$User,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Assert-PostgresIdentifier {
    <# Rejects identifiers that cannot be passed safely as individual PostgreSQL CLI arguments. #>
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Name)
    if ($Value -cnotmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "$Name must be a PostgreSQL identifier containing 1-63 ASCII letters, digits, or underscores and starting with a letter or underscore."
    }
}

Assert-PostgresIdentifier -Value $Database -Name 'Database'
Assert-PostgresIdentifier -Value $User -Name 'User'
if ($ContainerName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$') {
    throw 'ContainerName contains unsupported characters.'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if ([System.IO.Directory]::Exists($outputDirectory) -eq $false) {
    throw "Output directory does not exist: $outputDirectory"
}
if ([System.IO.File]::Exists($resolvedOutput)) {
    throw "Refusing to overwrite existing backup: $resolvedOutput"
}

$guid = [Guid]::NewGuid().ToString('N')
$remotePath = "/tmp/sonosmonitoring-pg-backup-$guid.dump"
$localTempPath = Join-Path $outputDirectory ".sonosmonitoring-pg-backup-$guid.tmp"
$remoteCreated = $false
$localCreated = $false
try {
    if ([System.IO.File]::Exists($localTempPath) -or [System.IO.Directory]::Exists($localTempPath)) {
        throw 'Generated local staging path already exists; refusing to modify it.'
    }
    & docker exec $ContainerName test '!' '-e' $remotePath *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not prove the generated container staging path is absent.' }
    $remoteCreated = $true
    $dumpArguments = @('exec', $ContainerName, 'pg_dump', '-U', $User, '-d', $Database, '-Fc', '-f', $remotePath)
    & docker @dumpArguments
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }

    $copyArguments = @('cp', "${ContainerName}:$remotePath", $localTempPath)
    & docker @copyArguments
    if ($LASTEXITCODE -ne 0) { throw "docker cp failed with exit code $LASTEXITCODE." }
    $localCreated = $true

    [System.IO.File]::Move($localTempPath, $resolvedOutput)
    $localCreated = $false
    $hash = Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256
    Write-Output "Backup created: $resolvedOutput"
    Write-Output "SHA256: $($hash.Hash)"
}
finally {
    if ($remoteCreated) {
        & docker exec $ContainerName rm -f -- $remotePath *> $null
    }
    if ($localCreated -and [System.IO.File]::Exists($localTempPath)) {
        Remove-Item -LiteralPath $localTempPath -Force
    }
}
