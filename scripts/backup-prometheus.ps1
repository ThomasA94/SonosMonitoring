<#
.SYNOPSIS
Creates a Prometheus TSDB snapshot in a new local directory.

.DESCRIPTION
Calls the explicitly enabled Prometheus admin snapshot API, then transfers the snapshot
directory with docker cp. The script never enables the admin API or restarts a container.
The destination directory must not exist. The API URL must use loopback and match the named
container's loopback-published web port.

.PARAMETER ContainerName
Name of the running Prometheus container containing the snapshot.

.PARAMETER AdminUrl
Loopback base URL for the Prometheus admin API, such as http://127.0.0.1:48090.

.PARAMETER OutputDirectory
New directory to receive the snapshot. Its parent must exist and the directory must be absent.

.EXAMPLE
./scripts/backup-prometheus.ps1 -ContainerName sonosmonitoring-metrics-prometheus-1 -AdminUrl http://127.0.0.1:48090 -OutputDirectory D:\Backups\metrics-20260929
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ContainerName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$AdminUrl,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($ContainerName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$') {
    throw 'ContainerName contains unsupported characters.'
}
$adminUri = [Uri]$AdminUrl
if ($adminUri.Scheme -cne 'http' -or $adminUri.Host -cnotin @('localhost', '127.0.0.1', '::1') -or
    $adminUri.UserInfo -ne '' -or $adminUri.Query -ne '' -or $adminUri.Fragment -ne '' -or
    $adminUri.AbsolutePath -cnotin @('', '/')) {
    throw 'AdminUrl must be an HTTP loopback base URL without credentials, path, query, or fragment.'
}
$baseUrl = $adminUri.GetLeftPart([System.UriPartial]::Authority).TrimEnd('/')
$portMappings = & docker port $ContainerName '9090/tcp'
if ($LASTEXITCODE -ne 0) { throw 'Could not verify the named container published its Prometheus web port.' }
$matchingPort = $false
foreach ($mapping in $portMappings) {
    if ($mapping -match '^(127\.0\.0\.1|\[::1\]):(\d+)$' -and [int]$Matches[2] -eq $adminUri.Port) {
        $matchingPort = $true
    }
}
if ($matchingPort -eq $false) { throw 'AdminUrl port does not match a loopback-only publication of the named Prometheus container.' }
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$outputParent = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if ([System.IO.Directory]::Exists($outputParent) -eq $false) { throw "Output parent does not exist: $outputParent" }
if ([System.IO.Directory]::Exists($resolvedOutput) -or [System.IO.File]::Exists($resolvedOutput)) {
    throw "Refusing to overwrite existing output: $resolvedOutput"
}

$guid = [Guid]::NewGuid().ToString('N')
$stagingDirectory = Join-Path $outputParent ".sonosmonitoring-prometheus-$guid.tmp"
$stagingCreated = $false
$snapshotName = $null
$snapshotPath = $null
$snapshotRemoved = $false
try {
    if ([System.IO.Directory]::Exists($stagingDirectory) -or [System.IO.File]::Exists($stagingDirectory)) {
        throw 'Generated staging path already exists; refusing to modify it.'
    }
    $response = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/v1/admin/tsdb/snapshot" -TimeoutSec 30 -MaximumRedirection 0
    if ($response.status -cne 'success' -or $response.data.name -isnot [string] -or
        $response.data.name -cnotmatch '^[A-Za-z0-9_-]{1,128}$') {
        throw 'Prometheus returned an invalid TSDB snapshot response.'
    }

    $snapshotName = $response.data.name
    $snapshotPath = "/prometheus/snapshots/$snapshotName"
    New-Item -ItemType Directory -Path $stagingDirectory -ErrorAction Stop | Out-Null
    $stagingCreated = $true
    & docker cp "${ContainerName}:$snapshotPath" $stagingDirectory
    if ($LASTEXITCODE -ne 0) { throw "docker cp failed with exit code $LASTEXITCODE." }
    $snapshotRoot = Join-Path $stagingDirectory $snapshotName
    if ([System.IO.Directory]::Exists($snapshotRoot) -eq $false -or
        @(Get-ChildItem -LiteralPath $snapshotRoot -Filter 'meta.json' -File -Recurse).Count -eq 0) {
        $copiedPaths = Get-ChildItem -LiteralPath $stagingDirectory -Force -Recurse | ForEach-Object {
            [System.IO.Path]::GetRelativePath($stagingDirectory, $_.FullName)
        }
        throw "Copied snapshot is missing its expected metadata. Copied paths: $($copiedPaths -join ', ')"
    }

    [System.IO.Directory]::Move($snapshotRoot, $resolvedOutput)
    $hashRows = Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($resolvedOutput, $_.FullName)
        "$relativePath $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
    }
    $hashBytes = [System.Text.Encoding]::UTF8.GetBytes(($hashRows -join "`n"))
    $sha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($hashBytes))
    & docker exec $ContainerName rm -rf -- $snapshotPath *> $null
    if ($LASTEXITCODE -eq 0) { $snapshotRemoved = $true }
    Write-Output "Snapshot created: $resolvedOutput"
    Write-Output "Snapshot name: $snapshotName"
    Write-Output "Files: $($hashRows.Count)"
    Write-Output "Tree SHA256: $sha256"
}
finally {
    if ($snapshotName -and $snapshotRemoved -eq $false) {
        if ([System.IO.Directory]::Exists($resolvedOutput)) {
            Write-Warning "Backup data was copied, but source snapshot '$snapshotName' remains in the Prometheus container."
        }
        else {
            Write-Warning "Incomplete backup left source snapshot '$snapshotName' in the Prometheus container for inspection."
        }
    }
    $actualStagingParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetDirectoryName($stagingDirectory))
    if ($stagingCreated -and $actualStagingParent -ceq $outputParent -and [System.IO.Directory]::Exists($stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
