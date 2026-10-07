[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$suffix = [Guid]::NewGuid().ToString('N')
$volume = "sonosmonitoring-logs-test-$suffix"
$apiName = "$volume-api"
$webName = "$volume-web"
$testKey = 'isolated-log-test-key-01234567890123456789'
$createdContainers = @()
$volumeCreated = $false

function Invoke-Docker {
    <# Fails on the current Docker exit code and returns only requested command output. #>
    param([string[]]$Arguments)
    $result = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker operation failed: $($Arguments[0])" }
    return $result
}

function Read-TestLogs {
    <# Reads only this test API; no installation credentials or real device traffic are used. #>
    return Invoke-RestMethod -Uri "$script:apiAddress/api/diagnostics/logs?limit=500" -Headers @{ 'X-Api-Key' = $testKey } -TimeoutSec 3
}

try {
    $null = Invoke-Docker @('volume', 'create', '--label', 'sonosmonitoring.test=application-logs', $volume)
    $volumeCreated = $true
    foreach ($source in @('api', 'web', 'projections')) {
        $null = Invoke-Docker @('run', '--rm', '--mount', "type=volume,source=$volume,target=/app/application-logs",
            '--entrypoint', 'sh', "sonosmonitoring-${source}:local", '-c',
            'test "$(id -u)" -ne 0 && touch /app/application-logs/permissions-test')
    }
    $null = Invoke-Docker @('run', '-d', '--name', $apiName, '--label', 'sonosmonitoring.test=application-logs',
        '--mount', "type=volume,source=$volume,target=/app/application-logs", '-p', '127.0.0.1::8080',
        '-e', 'ApplicationLogs__Directory=/app/application-logs', '-e', "Access__ApiKey=$testKey",
        '-e', 'Collector__Enabled=false', '-e', 'Backend__Enabled=false', 'sonosmonitoring-api:local')
    $createdContainers += $apiName
    $binding = Invoke-Docker @('port', $apiName, '8080/tcp')
    $script:apiAddress = 'http://' + ($binding | Select-Object -First 1)
    $null = Invoke-Docker @('run', '-d', '--name', $webName, '--label', 'sonosmonitoring.test=application-logs',
        '--mount', "type=volume,source=$volume,target=/app/application-logs",
        '-e', 'ApplicationLogs__Directory=/app/application-logs', 'sonosmonitoring-web:local')
    $createdContainers += $webName
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    $before = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $before = Read-TestLogs
            if (@($before.entries | Where-Object source -eq 'api').Count -gt 0 -and
                @($before.entries | Where-Object source -eq 'web').Count -gt 0) { break }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    if ($null -eq $before -or @($before.entries | Where-Object source -eq 'web').Count -eq 0 -or
        @($before.entries | Where-Object source -eq 'api').Count -eq 0) {
        throw 'API did not read both API and web host logs from the shared volume.'
    }
    $retained = $before.entries | Where-Object source -eq 'api' | Select-Object -First 1
    $null = Invoke-Docker @('restart', $apiName)
    # Docker may allocate a different host port when restarting an ephemeral port binding.
    $restartedBinding = Invoke-Docker @('port', $apiName, '8080/tcp')
    $restartedAddress = 'http://' + ($restartedBinding | Select-Object -First 1)
    Write-Output "Restart diagnostic: ephemeral port changed=$($script:apiAddress -ne $restartedAddress)."
    $script:apiAddress = $restartedAddress
    $after = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try { $after = Read-TestLogs; break } catch { Start-Sleep -Seconds 1 }
    }
    if ($null -eq $after) { throw 'API did not become reachable after container restart.' }
    if (@($after.entries | Where-Object {
            $_.source -eq 'api' -and $_.timestampUtc -eq $retained.timestampUtc -and $_.message -eq $retained.message
        }).Count -eq 0) {
        $sameTimestamp = @($after.entries | Where-Object { $_.source -eq 'api' -and $_.timestampUtc -eq $retained.timestampUtc }).Count
        throw "Previously retained API log disappeared after container restart. Before entries=$(@($before.entries).Count); after entries=$(@($after.entries).Count); timestamp matches=$sameTimestamp."
    }
    Write-Output 'Application log container checks passed: all three images write as non-root; API reads web logs; history survives API restart.'
}
finally {
    foreach ($container in $createdContainers) {
        $null = Invoke-Docker @('rm', '-f', $container)
    }
    if ($volumeCreated) { $null = Invoke-Docker @('volume', 'rm', $volume) }
}
