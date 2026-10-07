[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'resolve-prometheus-image.ps1')
$image = Get-PinnedPrometheusImage
$guid = [Guid]::NewGuid().ToString('N')
$sourceContainer = "sonosmonitoring-tsdb-source-$guid"
$restoredContainer = "sonosmonitoring-tsdb-restored-$guid"
$sourceVolume = "sonosmonitoring-tsdb-source-$guid"
$restoredVolume = "sonosmonitoring-tsdb-restored-$guid"
$testRoot = Join-Path $repoRoot 'artifacts/test-results'
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "sonosmonitoring-tsdb-test-$guid"
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$backupDirectory = Join-Path $testDirectory 'snapshot'
$configPath = Join-Path $testDirectory 'prometheus.yml'
$logPath = Join-Path $testRoot "prometheus-backup-$guid.log"
$script:logLines = [System.Collections.Generic.List[string]]::new()
$sourceVolumeCreated = $false
$restoredVolumeCreated = $false
$sourceContainerCreated = $false
$restoredContainerCreated = $false

function Invoke-DockerChecked {
    <# Runs one Docker command and records output for this isolated test. #>
    param([Parameter(Mandatory)][string[]]$Arguments)
    $script:logLines.Add("docker $($Arguments -join ' ')")
    $output = & docker @Arguments 2>&1
    $script:logLines.Add(($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed with exit code $LASTEXITCODE." }
    return ($output -join '').Trim()
}

function Assert-DockerResourceAbsent {
    <# Refuses to use a generated name already present in the Docker engine. #>
    param([Parameter(Mandatory)][ValidateSet('container', 'volume')][string]$Kind, [Parameter(Mandatory)][string]$Name)
    if ($Kind -ceq 'container') { & docker container inspect $Name *> $null }
    else { & docker volume inspect $Name *> $null }
    if ($LASTEXITCODE -eq 0) { throw "Generated Docker $Kind name already exists: $Name" }
    if ($LASTEXITCODE -ne 1) { throw "Could not verify Docker $Kind absence: $Name" }
}

function Get-PrometheusPort {
    <# Reads the random loopback-bound port assigned by Docker. #>
    param([Parameter(Mandatory)][string]$Container)
    $portOutput = & docker port $Container '9090/tcp'
    if ($LASTEXITCODE -ne 0 -or ($portOutput -join '') -cnotmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Could not read the loopback-bound Prometheus port.'
    }
    return [int]$Matches[1]
}

function Get-QueryRange {
    <# Reads the selected historical range from the given Prometheus endpoint. #>
    param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][long]$Start, [Parameter(Mandatory)][long]$End)
    $query = 'prometheus_build_info'
    $uri = "$BaseUrl/api/v1/query_range?query=$([Uri]::EscapeDataString($query))&start=$Start&end=$End&step=2"
    $response = Invoke-RestMethod -Method Get -Uri $uri -TimeoutSec 10
    if ($response.status -cne 'success' -or $response.data.result.Count -eq 0) { throw 'The expected metric history was not present.' }
    return ($response.data.result | ConvertTo-Json -Depth 12 -Compress)
}

try {
    Assert-DockerResourceAbsent -Kind container -Name $sourceContainer
    Assert-DockerResourceAbsent -Kind container -Name $restoredContainer
    Assert-DockerResourceAbsent -Kind volume -Name $sourceVolume
    Assert-DockerResourceAbsent -Kind volume -Name $restoredVolume

    $yaml = @'
global:
  scrape_interval: 2s
  evaluation_interval: 2s
scrape_configs:
  - job_name: self
    static_configs:
      - targets: ["localhost:9090"]
'@
    [System.IO.File]::WriteAllText($configPath, $yaml, [System.Text.UTF8Encoding]::new($false))

    $sourceVolumeCreated = $true
    Invoke-DockerChecked -Arguments @('volume', 'create', '--label', "sonosmonitoring.test=$guid", $sourceVolume) | Out-Null
    $sourceContainerCreated = $true
    Invoke-DockerChecked -Arguments @('run', '--detach', '--name', $sourceContainer, '--label', "sonosmonitoring.test=$guid", '--publish', '127.0.0.1::9090', '--mount', "type=volume,source=$sourceVolume,target=/prometheus", '--mount', "type=bind,source=$configPath,target=/etc/prometheus/prometheus.yml,readonly", $image, '--config.file=/etc/prometheus/prometheus.yml', '--storage.tsdb.path=/prometheus', '--storage.tsdb.retention.time=1h', '--web.enable-admin-api', '--web.listen-address=0.0.0.0:9090') | Out-Null
    $sourcePort = Get-PrometheusPort -Container $sourceContainer
    $sourceUrl = "http://127.0.0.1:$sourcePort"

    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $probe = Invoke-RestMethod -Method Get -Uri "$sourceUrl/-/ready" -TimeoutSec 2
            if ($probe.StatusCode -eq 200 -or $probe -is [string]) { $ready = $true; break }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ($ready -eq $false) { throw 'The source Prometheus instance did not become ready within 90 seconds.' }

    Start-Sleep -Seconds 20
    $endTime = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $startTime = $endTime - 18
    $sourceRange = Get-QueryRange -BaseUrl $sourceUrl -Start $startTime -End $endTime
    $backupOutput = & (Join-Path $PSScriptRoot 'backup-prometheus.ps1') -ContainerName $sourceContainer -AdminUrl $sourceUrl -OutputDirectory $backupDirectory 2>&1
    $script:logLines.Add(($backupOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
    if ([System.IO.Directory]::Exists($backupDirectory) -eq $false) { throw 'Backup script did not create the snapshot directory.' }
    $snapshotNameLine = $backupOutput | ForEach-Object { $_.ToString() } | Where-Object { $_ -match '^Snapshot name: ([A-Za-z0-9_-]{1,128})$' } | Select-Object -First 1
    if ($null -eq $snapshotNameLine) { throw 'Backup output did not include the created snapshot name.' }
    $snapshotName = [regex]::Match($snapshotNameLine, '^Snapshot name: ([A-Za-z0-9_-]{1,128})$').Groups[1].Value
    & docker exec $sourceContainer test '!' '-e' "/prometheus/snapshots/$snapshotName" *> $null
    if ($LASTEXITCODE -ne 0) { throw 'The validated source snapshot was not removed after backup transfer.' }
    $snapshotMetadata = Get-ChildItem -LiteralPath $backupDirectory -Filter 'meta.json' -File -Recurse | Select-Object -First 1
    $script:logLines.Add("Snapshot manifest SHA256: $((Get-FileHash -LiteralPath $snapshotMetadata.FullName -Algorithm SHA256).Hash)")

    $restoredVolumeCreated = $true
    $restoreOutput = & (Join-Path $PSScriptRoot 'restore-prometheus-volume.ps1') -BackupDirectory $backupDirectory -VolumeName $restoredVolume 2>&1
    $restoreOutput | ForEach-Object { $script:logLines.Add($_.ToString()) }
    if ($LASTEXITCODE -ne 0) { throw "restore-prometheus-volume.ps1 failed with exit code $LASTEXITCODE." }
    $restoreOwnerLine = $restoreOutput | ForEach-Object { $_.ToString() } | Where-Object { $_ -match '^Restore owner: ([a-f0-9]{32})$' } | Select-Object -First 1
    if ($null -eq $restoreOwnerLine) { throw 'Restore did not report its unique volume ownership label.' }
    $restoredVolumeOwner = [regex]::Match($restoreOwnerLine, '^Restore owner: ([a-f0-9]{32})$').Groups[1].Value

    $restoredContainerCreated = $true
    Invoke-DockerChecked -Arguments @('run', '--detach', '--name', $restoredContainer, '--label', "sonosmonitoring.test=$guid", '--publish', '127.0.0.1::9090', '--mount', "type=volume,source=$restoredVolume,target=/prometheus", '--mount', "type=bind,source=$configPath,target=/etc/prometheus/prometheus.yml,readonly", $image, '--config.file=/etc/prometheus/prometheus.yml', '--storage.tsdb.path=/prometheus', '--storage.tsdb.retention.time=1h', '--web.listen-address=0.0.0.0:9090') | Out-Null
    $restoredPort = Get-PrometheusPort -Container $restoredContainer
    $restoredUrl = "http://127.0.0.1:$restoredPort"

    $restoredReady = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $probe = Invoke-RestMethod -Method Get -Uri "$restoredUrl/-/ready" -TimeoutSec 2
            if ($probe.StatusCode -eq 200 -or $probe -is [string]) { $restoredReady = $true; break }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ($restoredReady -eq $false) { throw 'The restored Prometheus instance did not become ready within 90 seconds.' }

    $restoredRange = Get-QueryRange -BaseUrl $restoredUrl -Start $startTime -End $endTime
    if ($restoredRange -cne $sourceRange) { throw 'The restored TSDB range did not match the source values.' }
    $script:logLines.Add("Verified metric history range: $startTime through $endTime")
    $script:logLines.Add("Metric range SHA256: $([Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($restoredRange))))")

    $collisionRejected = $false
    try {
        & (Join-Path $PSScriptRoot 'restore-prometheus-volume.ps1') -BackupDirectory $backupDirectory -VolumeName $restoredVolume *> $null
        if ($LASTEXITCODE -ne 0) { $collisionRejected = $true }
    }
    catch { $collisionRejected = $true }
    if ($collisionRejected -eq $false) { throw 'Restore unexpectedly accepted an existing destination volume.' }
    $preservedRange = Get-QueryRange -BaseUrl $restoredUrl -Start $startTime -End $endTime
    if ($preservedRange -cne $restoredRange) { throw 'The existing destination volume changed after rejected restore.' }
    $script:logLines.Add('Existing destination volume: restore rejected')
    $script:logLines.Add('Result: PASS')
}
catch {
    $script:logLines.Add("Result: FAIL - $($_.Exception.Message)")
    throw
}
finally {
    if ($sourceContainerCreated) {
        $owner = & docker container inspect --format '{{index .Config.Labels "sonosmonitoring.test"}}' $sourceContainer 2>$null
        if ($LASTEXITCODE -eq 0 -and ($owner -join '').Trim() -ceq $guid) { & docker rm --force $sourceContainer *> $null }
    }
    if ($restoredContainerCreated) {
        $owner = & docker container inspect --format '{{index .Config.Labels "sonosmonitoring.test"}}' $restoredContainer 2>$null
        if ($LASTEXITCODE -eq 0 -and ($owner -join '').Trim() -ceq $guid) { & docker rm --force $restoredContainer *> $null }
    }
    if ($sourceVolumeCreated) {
        $owner = & docker volume inspect --format '{{index .Labels "sonosmonitoring.test"}}' $sourceVolume 2>$null
        if ($LASTEXITCODE -eq 0 -and ($owner -join '').Trim() -ceq $guid) { & docker volume rm $sourceVolume *> $null }
    }
    if ($restoredVolumeCreated) {
        $owner = & docker volume inspect --format '{{index .Labels "sonosmonitoring.restore"}}' $restoredVolume 2>$null
        if ($LASTEXITCODE -eq 0 -and ($owner -join '').Trim() -ceq $restoredVolumeOwner) { & docker volume rm $restoredVolume *> $null }
    }
    if ([System.IO.Directory]::Exists($testDirectory)) { Remove-Item -LiteralPath $testDirectory -Recurse -Force }
    [System.IO.File]::WriteAllLines($logPath, $script:logLines)
    Write-Output "Prometheus backup test log: $logPath"
}
