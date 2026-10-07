[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$image = 'postgres:17.11-bookworm@sha256:051f7b7b3abdd564d5d1bd1e8c4b9c1b6e77087d1dd22020ede611c096a272e0'
$container = "sonosmonitoring-backup-test-$([Guid]::NewGuid().ToString('N'))"
$database = 'backup_source'
$restoredDatabase = 'backup_restored'
$user = 'backup_test'
$testRoot = Join-Path $repoRoot 'artifacts/test-results'
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$guid = [Guid]::NewGuid().ToString('N')
$backup = Join-Path ([System.IO.Path]::GetTempPath()) "sonosmonitoring-backup-$guid.dump"
$log = Join-Path $testRoot "postgres-backup-$guid.log"
$containerStarted = $false
$script:logLines = [System.Collections.Generic.List[string]]::new()
$mediaAcceptedId = [Guid]::NewGuid().ToString('D')
$mediaStartedId = [Guid]::NewGuid().ToString('D')
$mediaAcknowledgedId = [Guid]::NewGuid().ToString('D')
$mediaUnknownId = [Guid]::NewGuid().ToString('D')
$mediaCompletedId = [Guid]::NewGuid().ToString('D')
$mediaOriginalGeneration = [Guid]::NewGuid().ToString('D')

function Invoke-DockerChecked {
    <# Runs one Docker command and records its arguments without exposing credentials. #>
    param([Parameter(Mandatory)][string[]]$Arguments)
    $script:logLines.Add("docker $($Arguments -join ' ')")
    & docker @Arguments 2>&1 | ForEach-Object { $script:logLines.Add($_.ToString()) }
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed with exit code $LASTEXITCODE." }
}

function Invoke-ContainerPsql {
    <# Executes SQL using the container's local PostgreSQL authentication. #>
    param([Parameter(Mandatory)][string]$DatabaseName, [Parameter(Mandatory)][string]$Sql)
    $arguments = @('exec', $container, 'psql', '-X', '-q', '-v', 'ON_ERROR_STOP=1', '-U', $user, '-d', $DatabaseName, '-At', '-c', $Sql)
    $script:logLines.Add("docker $($arguments -join ' ')")
    $result = & docker @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $script:logLines.Add(($result | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
        throw "psql failed with exit code $LASTEXITCODE."
    }
    return ($result -join [Environment]::NewLine).Trim()
}

try {
    $containerStarted = $true
    Invoke-DockerChecked -Arguments @('run', '--detach', '--name', $container, '--env', 'POSTGRES_USER=backup_test', '--env', 'POSTGRES_DB=postgres', '--env', 'POSTGRES_HOST_AUTH_METHOD=trust', $image)

    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        & docker exec $container pg_isready -U $user -d postgres *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if ($ready -eq $false) {
        $containerLog = & docker logs $container 2>&1
        $script:logLines.Add(($containerLog | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
        throw 'The isolated PostgreSQL container did not become ready within 90 seconds.'
    }

    Invoke-DockerChecked -Arguments @('exec', $container, 'createdb', '-U', $user, '-T', 'template0', $database)
    $seedSql = @'
CREATE TABLE backup_probe (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    external_key text NOT NULL UNIQUE,
    payload jsonb NOT NULL CHECK (payload ? 'kind')
);
INSERT INTO backup_probe (external_key, payload)
VALUES ('known-row', '{"kind":"restore-proof","value":42}'::jsonb);
'@
    Invoke-ContainerPsql -DatabaseName $database -Sql $seedSql | Out-Null

    & (Join-Path $PSScriptRoot 'initialize-media.ps1') -ContainerName $container -Database $database -User $user 2>&1 | ForEach-Object { $script:logLines.Add($_.ToString()) }
    if ($LASTEXITCODE -ne 0) { throw "initialize-media.ps1 failed with exit code $LASTEXITCODE." }
    $freshGrant = Invoke-ContainerPsql -DatabaseName $database -Sql 'SELECT version::text || ''|'' || enabled::text FROM sonos_media.version CROSS JOIN sonos_media.control WHERE sonos_media.version.singleton AND sonos_media.control.singleton'
    if ($freshGrant -cne '1|false') { throw "Fresh media grant was not disabled at schema version 1: $freshGrant" }
    $freshMediaGeneration = Invoke-ContainerPsql -DatabaseName $database -Sql 'SELECT generation::text FROM sonos_media.control WHERE singleton'

    $existingMediaRejected = $false
    try {
        & (Join-Path $PSScriptRoot 'initialize-media.ps1') -ContainerName $container -Database $database -User $user *> $null
        if ($LASTEXITCODE -ne 0) { $existingMediaRejected = $true }
    }
    catch { $existingMediaRejected = $true }
    if ($existingMediaRejected -eq $false) { throw 'Media initialization unexpectedly accepted an existing namespace.' }
    $initialMediaRows = Invoke-ContainerPsql -DatabaseName $database -Sql 'SELECT count(*) FROM sonos_media.operations'
    if ($initialMediaRows -cne '0') { throw 'Rejected media reinitialization modified operation rows.' }
    $grantAfterRejection = Invoke-ContainerPsql -DatabaseName $database -Sql 'SELECT enabled::text || ''|'' || generation::text FROM sonos_media.control WHERE singleton'
    if ($grantAfterRejection -cne "false|$freshMediaGeneration") { throw 'Rejected media reinitialization changed the disabled grant.' }

    $mediaSeedSql = @"
UPDATE sonos_media.control SET enabled=true, generation='$mediaOriginalGeneration'::uuid WHERE singleton;
INSERT INTO sonos_media.operations
    (id,fingerprint,generation,targets,payload,accepted_at,expires_at,status,step,code,risk_released,outcome_recorded)
VALUES
    ('$mediaAcceptedId'::uuid,repeat('a',64),'$mediaOriginalGeneration'::uuid,ARRAY['RINCON_TEST_A'],'{}'::jsonb,clock_timestamp(),clock_timestamp()+interval '1 hour','accepted',-1,NULL,false,false),
    ('$mediaStartedId'::uuid,repeat('b',64),'$mediaOriginalGeneration'::uuid,ARRAY['RINCON_TEST_A'],'{}'::jsonb,clock_timestamp(),clock_timestamp()+interval '1 hour','dispatch_started',0,NULL,false,false),
    ('$mediaAcknowledgedId'::uuid,repeat('c',64),'$mediaOriginalGeneration'::uuid,ARRAY['RINCON_TEST_A'],'{}'::jsonb,clock_timestamp(),clock_timestamp()+interval '1 hour','device_acknowledged',0,NULL,false,false),
    ('$mediaUnknownId'::uuid,repeat('d',64),'$mediaOriginalGeneration'::uuid,ARRAY['RINCON_TEST_A'],'{}'::jsonb,clock_timestamp(),clock_timestamp()+interval '1 hour','outcome_unknown',0,'media.outcome.unknown',true,true),
    ('$mediaCompletedId'::uuid,repeat('e',64),'$mediaOriginalGeneration'::uuid,ARRAY['RINCON_TEST_A'],'{}'::jsonb,clock_timestamp(),clock_timestamp()+interval '1 hour','completed',0,'media.device.acknowledged',false,true);
INSERT INTO sonos_media.steps(operation,step) VALUES
    ('$mediaStartedId'::uuid,0),('$mediaAcknowledgedId'::uuid,0),('$mediaUnknownId'::uuid,0),('$mediaCompletedId'::uuid,0);
"@
    Invoke-ContainerPsql -DatabaseName $database -Sql $mediaSeedSql | Out-Null

    & (Join-Path $PSScriptRoot 'backup-postgres.ps1') -ContainerName $container -Database $database -User $user -OutputPath $backup 2>&1 | ForEach-Object { $script:logLines.Add($_.ToString()) }
    if ($LASTEXITCODE -ne 0) { throw "backup-postgres.ps1 failed with exit code $LASTEXITCODE." }
    if ([System.IO.File]::Exists($backup) -eq $false) { throw 'Backup script did not create the requested dump.' }
    $hash = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash
    $script:logLines.Add("Backup SHA256: $hash")

    & (Join-Path $PSScriptRoot 'restore-postgres.ps1') -ContainerName $container -User $user -Database $restoredDatabase -BackupPath $backup 2>&1 | ForEach-Object { $script:logLines.Add($_.ToString()) }
    if ($LASTEXITCODE -ne 0) { throw "restore-postgres.ps1 failed with exit code $LASTEXITCODE." }

    $restored = Invoke-ContainerPsql -DatabaseName $restoredDatabase -Sql "SELECT id::text || '|' || external_key || '|' || (payload->>'kind') || '|' || (payload->>'value') FROM backup_probe WHERE id = 1"
    if ($restored -cne '1|known-row|restore-proof|42') { throw "Restored row did not match the seed: $restored" }
    $script:logLines.Add("Restored row: $restored")

    $restoredGrant = Invoke-ContainerPsql -DatabaseName $restoredDatabase -Sql 'SELECT enabled::text || ''|'' || generation::text || ''|'' || (restored_at IS NOT NULL)::text FROM sonos_media.control WHERE singleton'
    $restoredGrantParts = $restoredGrant.Split('|')
    if ($restoredGrantParts.Count -ne 3 -or $restoredGrantParts[0] -cne 'false' -or
        $restoredGrantParts[1] -ceq $mediaOriginalGeneration -or $restoredGrantParts[2] -cne 'true') {
        throw "Restored media grant was not disabled with a fresh generation and timestamp: $restoredGrant"
    }
    $mediaStateQuery = "SELECT id::text || '|' || status || '|' || coalesce(code,'') || '|' || risk_released::text || '|' || outcome_recorded::text || '|' || (SELECT count(*) FROM sonos_media.steps s WHERE s.operation=o.id)::text FROM sonos_media.operations o ORDER BY id"
    $mediaStates = @((Invoke-ContainerPsql -DatabaseName $restoredDatabase -Sql $mediaStateQuery) -split '\r?\n')
    $expectedMedia = @{
        $mediaAcceptedId = 'revoked|media.restore.revoked|false|false|0'
        $mediaStartedId = 'outcome_unknown|media.restore.uncertain|false|false|1'
        $mediaAcknowledgedId = 'outcome_unknown|media.restore.uncertain|false|false|1'
        $mediaUnknownId = 'outcome_unknown|media.restore.uncertain|false|false|1'
        $mediaCompletedId = 'completed|media.device.acknowledged|false|true|1'
    }
    if ($mediaStates.Count -ne $expectedMedia.Count) { throw "Restored media operation count changed: $($mediaStates.Count)" }
    foreach ($mediaState in $mediaStates) {
        $separator = $mediaState.IndexOf('|')
        if ($separator -lt 0) { throw "Malformed restored media state: $mediaState" }
        $operationId = $mediaState.Substring(0, $separator)
        $details = $mediaState.Substring($separator + 1)
        if ($expectedMedia.ContainsKey($operationId) -eq $false -or $details -cne $expectedMedia[$operationId]) {
            throw "Restored media state diverged for $operationId : $details"
        }
    }
    $script:logLines.Add('Media restore: grant revoked, original generation replaced, uncertain targets blocked, completed step retained')

    $nextIdentity = Invoke-ContainerPsql -DatabaseName $restoredDatabase -Sql 'INSERT INTO backup_probe (external_key, payload) VALUES (''next-row'', ''{"kind":"identity-check"}''::jsonb) RETURNING id'
    if ($nextIdentity -cne '2') { throw "Identity sequence was not restored correctly; next id was $nextIdentity." }
    $script:logLines.Add("Next identity: $nextIdentity")

    $constraintRejected = $false
    try {
        Invoke-ContainerPsql -DatabaseName $restoredDatabase -Sql 'INSERT INTO backup_probe (external_key, payload) VALUES (''known-row'', ''{"kind":"duplicate"}''::jsonb)' | Out-Null
    }
    catch { $constraintRejected = $true }
    if ($constraintRejected -eq $false) { throw 'The restored uniqueness constraint did not reject a duplicate key.' }
    $script:logLines.Add('Unique constraint: duplicate rejected')

    $existingRejected = $false
    try {
        & (Join-Path $PSScriptRoot 'restore-postgres.ps1') -ContainerName $container -User $user -Database $restoredDatabase -BackupPath $backup *> $null
        if ($LASTEXITCODE -ne 0) { $existingRejected = $true }
    }
    catch { $existingRejected = $true }
    if ($existingRejected -eq $false) { throw 'Restore unexpectedly accepted an existing destination database.' }
    $preservedCount = Invoke-ContainerPsql -DatabaseName $restoredDatabase -Sql 'SELECT count(*) FROM backup_probe'
    if ($preservedCount -cne '2') { throw "Existing destination changed after rejected restore; row count was $preservedCount." }
    $script:logLines.Add('Existing destination: restore rejected and existing rows preserved')
    $script:logLines.Add('Result: PASS')
}
catch {
    $script:logLines.Add("Result: FAIL - $($_.Exception.Message)")
    throw
}
finally {
    if ($containerStarted) {
        & docker rm --force $container *> $null
        if ($LASTEXITCODE -ne 0) { $script:logLines.Add('Cleanup warning: failed to remove the isolated test container.') }
    }
    if ([System.IO.File]::Exists($backup)) { Remove-Item -LiteralPath $backup -Force }
    [System.IO.File]::WriteAllLines($log, $script:logLines)
    Write-Output "Backup test log: $log"
}
