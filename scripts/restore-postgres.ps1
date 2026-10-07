<#
.SYNOPSIS
Restores a PostgreSQL custom-format backup into a new database in a running Docker container.

.DESCRIPTION
The destination is checked for absence before creation. Existing databases are never dropped,
cleaned, or overwritten. A failed restore leaves its newly created database for inspection.
Object ownership and grants are omitted; restore with the supplied role using local container auth.

.PARAMETER ContainerName
Name of the running PostgreSQL container.

.PARAMETER User
PostgreSQL role used to check/create the database and run the restore.

.PARAMETER Database
New destination database identifier; it must not already exist.

.PARAMETER BackupPath
Existing PostgreSQL custom-format backup file.

.EXAMPLE
./scripts/restore-postgres.ps1 -ContainerName sonos-postgres -User postgres -Database app_restore -BackupPath D:\Backups\app.dump
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ContainerName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$User,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Database,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$BackupPath
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
$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath -ErrorAction Stop).Path
if ([System.IO.File]::Exists($resolvedBackup) -eq $false) { throw "Backup file does not exist: $resolvedBackup" }

$guid = [Guid]::NewGuid().ToString('N')
$remotePath = "/tmp/sonosmonitoring-pg-restore-$guid.dump"
$remoteCreated = $false
$databaseCreated = $false
try {
    $query = "SELECT 1 FROM pg_database WHERE datname = '$Database'"
    $checkArguments = @('exec', $ContainerName, 'psql', '-X', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-U', $User, '-d', 'postgres', '-c', $query)
    $existing = & docker @checkArguments
    if ($LASTEXITCODE -ne 0) { throw "Could not check destination database; psql exited with code $LASTEXITCODE." }
    if (($existing -join '').Trim() -eq '1') {
        throw "Refusing to restore over existing database '$Database'. Choose a new, absent database name."
    }

    & docker exec $ContainerName test '!' '-e' $remotePath *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not prove the generated container staging path is absent.' }
    $remoteCreated = $true
    $copyArguments = @('cp', $resolvedBackup, "${ContainerName}:$remotePath")
    & docker @copyArguments
    if ($LASTEXITCODE -ne 0) { throw "docker cp failed with exit code $LASTEXITCODE." }

    $createArguments = @('exec', $ContainerName, 'createdb', '-U', $User, '-T', 'template0', $Database)
    & docker @createArguments
    if ($LASTEXITCODE -ne 0) { throw "Could not create the new destination database; createdb exited with code $LASTEXITCODE." }
    $databaseCreated = $true

    $restoreArguments = @('exec', $ContainerName, 'pg_restore', '--exit-on-error', '--no-owner', '--no-privileges', '-U', $User, '-d', $Database, $remotePath)
    & docker @restoreArguments
    if ($LASTEXITCODE -ne 0) {
        throw "pg_restore failed with exit code $LASTEXITCODE. The newly created database '$Database' is preserved for inspection."
    }
    $mediaNamespace = & docker exec $ContainerName psql -X -A -t -v ON_ERROR_STOP=1 -U $User -d $Database -c "SELECT to_regnamespace('sonos_media') IS NOT NULL"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect media coordination after restore; the new database is preserved for inspection.' }
    if (($mediaNamespace -join '').Trim() -ceq 't') {
        $mediaRestoreSql = @'
BEGIN;
SELECT pg_advisory_xact_lock(731905240013);
DO $media_restore$
BEGIN
    IF to_regclass('sonos_media.version') IS NULL OR
       to_regclass('sonos_media.control') IS NULL OR
       to_regclass('sonos_media.operations') IS NULL OR
       to_regclass('sonos_media.steps') IS NULL OR
       to_regclass('sonos_media.target_epochs') IS NULL OR
       to_regclass('sonos_media.decisions') IS NULL THEN
        RAISE EXCEPTION 'Incomplete sonos_media schema; restore requires version 1';
    END IF;
    IF (SELECT count(*) FROM pg_tables WHERE schemaname='sonos_media') <> 6 THEN
        RAISE EXCEPTION 'Unexpected sonos_media table set; restore requires version 1';
    END IF;
    IF (SELECT count(*) FROM sonos_media.version WHERE singleton AND version=1) <> 1 OR
       (SELECT count(*) FROM sonos_media.version) <> 1 OR
       (SELECT count(*) FROM sonos_media.control WHERE singleton) <> 1 THEN
        RAISE EXCEPTION 'Unsupported sonos_media version or control row';
    END IF;
END
$media_restore$;
SELECT singleton FROM sonos_media.control WHERE singleton FOR UPDATE;
UPDATE sonos_media.control
SET enabled=false, generation=gen_random_uuid(), restored_at=clock_timestamp()
WHERE singleton;
UPDATE sonos_media.operations
SET status='outcome_unknown', code='media.restore.uncertain', risk_released=false, outcome_recorded=false
WHERE status IN ('dispatch_started','device_acknowledged','outcome_unknown');
UPDATE sonos_media.operations
SET status='revoked', code='media.restore.revoked', outcome_recorded=false
WHERE status='accepted';
COMMIT;
'@
        $mediaRestoreSql | & docker exec -i $ContainerName psql -X -v ON_ERROR_STOP=1 -U $User -d $Database -f -
        if ($LASTEXITCODE -ne 0) { throw 'Media restore safety transition failed. The new database is preserved for inspection and must not be started.' }
        Write-Output 'Restored media control disabled; in-flight writes marked uncertain and accepted requests revoked.'
    }
    elseif (($mediaNamespace -join '').Trim() -cne 'f') {
        throw 'Could not determine restored media coordination namespace state; the new database is preserved for inspection.'
    }
    Write-Output "Restore completed into newly created database '$Database'."
}
finally {
    if ($remoteCreated) {
        & docker exec $ContainerName rm -f -- $remotePath *> $null
    }
}
