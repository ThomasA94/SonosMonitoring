<#
.SYNOPSIS
Copies a saved Prometheus TSDB snapshot into a newly created Docker volume.

.DESCRIPTION
The volume must be absent. The script never removes or clears a volume. A short-lived helper
container from the pinned Prometheus image copies files and gives them to runtime UID 65534.
Start Prometheus separately with the new volume mounted at its configured TSDB path.

.PARAMETER BackupDirectory
Directory containing the snapshot files, including meta.json.

.PARAMETER VolumeName
New Docker volume name. Existing volumes are rejected.

.EXAMPLE
./scripts/restore-prometheus-volume.ps1 -BackupDirectory D:\Backups\metrics-20260929 -VolumeName sonosmonitoring-metrics-restored-20260929
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$BackupDirectory,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$VolumeName
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'resolve-prometheus-image.ps1')
$image = Get-PinnedPrometheusImage
if ($VolumeName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$') { throw 'VolumeName contains unsupported characters.' }
$resolvedBackup = (Resolve-Path -LiteralPath $BackupDirectory -ErrorAction Stop).Path
if ([System.IO.Directory]::Exists($resolvedBackup) -eq $false -or
    @(Get-ChildItem -LiteralPath $resolvedBackup -Filter 'meta.json' -File -Recurse).Count -eq 0) {
    throw 'BackupDirectory must contain a Prometheus TSDB snapshot with meta.json.'
}

$inspectOutput = & docker volume inspect $VolumeName 2>$null
if ($LASTEXITCODE -eq 0) { throw "Refusing to overwrite existing Docker volume '$VolumeName'." }
if ($LASTEXITCODE -ne 1) { throw 'Could not verify Docker volume absence.' }

$resolvedForDocker = $resolvedBackup.Replace('\', '/')
$volumeOwnerId = [Guid]::NewGuid().ToString('N')
$volumeOwner = "sonosmonitoring.restore=$volumeOwnerId"
& docker volume create --label $volumeOwner $VolumeName | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not create the new Docker volume.' }
$volumeLabelsJson = & docker volume inspect --format '{{json .Labels}}' $VolumeName
if ($LASTEXITCODE -ne 0) { throw "Could not verify ownership label on new volume '$VolumeName'." }
$volumeLabels = $volumeLabelsJson | ConvertFrom-Json
if ($volumeLabels.'sonosmonitoring.restore' -cne $volumeOwnerId) {
    throw "Docker volume '$VolumeName' was not created by this restore operation; refusing to copy files."
}

$helperName = "sonosmonitoring-tsdb-restore-$([Guid]::NewGuid().ToString('N'))"
$mountBackup = "type=bind,source=$resolvedForDocker,target=/backup,readonly"
$mountVolume = "type=volume,source=$VolumeName,target=/restore"
$helperArguments = @(
    'run', '--rm', '--name', $helperName, '--user', '0',
    '--mount', $mountBackup, '--mount', $mountVolume,
    '--entrypoint', '/bin/sh', $image, '-c', 'cp -a /backup/. /restore/ && chown -R 65534:65534 /restore'
)
& docker @helperArguments
if ($LASTEXITCODE -ne 0) {
    throw "Snapshot copy failed. The new volume '$VolumeName' is preserved for inspection."
}
Write-Output "Snapshot restored into new volume '$VolumeName'. Start Prometheus with this volume mounted at /prometheus."
Write-Output "Restore owner: $volumeOwnerId"
