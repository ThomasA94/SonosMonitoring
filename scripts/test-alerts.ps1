[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$rulesDirectory = Join-Path $repositoryRoot 'infra/prometheus'
$resultsDirectory = Join-Path $repositoryRoot 'artifacts/test-results'
$generatedDirectory = Join-Path $repositoryRoot 'artifacts/prometheus-tests'
New-Item -ItemType Directory -Force $resultsDirectory, $generatedDirectory | Out-Null

# Reuse the deployed image pin so rule semantics cannot drift between CI and runtime.
$composeText = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot 'infra/compose.metrics.yaml'))
$imageMatch = [regex]::Match($composeText, '(?m)^\s+image: (prom/prometheus:[^\s]+@sha256:[a-f0-9]{64})\s*$')
if ($imageMatch.Success -eq $false) {
    throw 'The metrics Compose file must pin the Prometheus image by digest.'
}
$prometheusImage = $imageMatch.Groups[1].Value

# Every application metric used in a rule must exist in the generated comparison catalogue.
$catalog = Get-Content -Raw (Join-Path $repositoryRoot 'contracts/metrics.json') | ConvertFrom-Json
$ruleText = [System.IO.File]::ReadAllText((Join-Path $rulesDirectory 'sonos-alerts.yml'))
foreach ($metricName in ([regex]::Matches($ruleText, '\bsonos_[a-z_]+') | ForEach-Object Value | Sort-Object -Unique)) {
    if ($metricName -cnotin $catalog.Name) {
        throw "Alert rules reference an undeclared collector metric: $metricName"
    }
}

$configurationPath = Join-Path $generatedDirectory 'prometheus.yml'
$keyPath = Join-Path $generatedDirectory 'api-key'
$template = [System.IO.File]::ReadAllText((Join-Path $rulesDirectory 'prometheus.yml.template'))
# The runtime probe never contacts a real collector or LAN address.
[System.IO.File]::WriteAllText($configurationPath, $template.Replace('__SCRAPE_TARGET__', '127.0.0.1:1'))
[System.IO.File]::WriteAllText($keyPath, 'isolated-test-only-no-real-credential')
$mounts = @(
    '--mount', "type=bind,source=$rulesDirectory,target=/work,readonly",
    '--mount', "type=bind,source=$configurationPath,target=/etc/prometheus/prometheus.yml,readonly",
    '--mount', "type=bind,source=$(Join-Path $rulesDirectory 'sonos-alerts.yml'),target=/etc/prometheus/sonos-alerts.yml,readonly",
    '--mount', "type=bind,source=$keyPath,target=/run/secrets/sonosmonitoring_api_key,readonly"
)

function Invoke-Promtool {
    <# Executes the pinned rule engine against read-only inputs without network access. #>
    param([Parameter(Mandatory)][string[]]$Arguments)
    & docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges `
        --tmpfs /tmp --workdir /work @mounts --entrypoint /bin/promtool $prometheusImage @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "promtool $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AlertsRequest {
    <# Sends one isolated API request and returns the status and bounded response text. #>
    param(
        [Parameter(Mandatory)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory)][string]$Uri,
        [string]$ApiKey
    )
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Uri)
    if ([string]::IsNullOrEmpty($ApiKey) -eq $false) {
        $request.Headers.Add('X-Api-Key', $ApiKey)
    }
    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($content.Length -gt 2MB) {
                throw 'The isolated alerts endpoint returned an oversized response.'
            }
            return [pscustomobject]@{
                StatusCode = [int]$response.StatusCode
                Content = $content
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Get-FreeLoopbackPort {
    <# Reserves an ephemeral loopback port long enough to discover its value. #>
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

Invoke-Promtool @('check', 'config', '/etc/prometheus/prometheus.yml')
Invoke-Promtool @('test', 'rules', 'sonos-alerts.tests.yml') |
    Tee-Object -FilePath (Join-Path $resultsDirectory 'alerts-promtool.log')

$containerName = 'sonosmonitoring-alert-tests-' + [Guid]::NewGuid().ToString('N')
try {
    & docker run --detach --name $containerName --read-only --cap-drop ALL --security-opt no-new-privileges `
        --tmpfs /prometheus:rw,nosuid,nodev,size=256m,mode=1777 --publish '127.0.0.1::9090' @mounts `
        $prometheusImage --config.file=/etc/prometheus/prometheus.yml --storage.tsdb.path=/prometheus | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to start the isolated Prometheus runtime probe.' }
    $binding = & docker port $containerName '9090/tcp'
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:[0-9]+$') {
        throw 'The runtime probe must expose exactly one loopback HTTP port.'
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $loadedRules = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-RestMethod -Uri "http://$binding/api/v1/rules" -TimeoutSec 3
            $rules = @($response.data.groups.rules)
            if ($response.status -eq 'success' -and $rules.Count -eq 8 -and
                @($rules | Where-Object { $_.health -ne 'ok' -or [DateTimeOffset]$_.lastEvaluation -lt [DateTimeOffset]::UtcNow.AddMinutes(-1) }).Count -eq 0) {
                $loadedRules = $response
                break
            }
        }
        catch {
            # A starting server may briefly reject HTTP connections.
        }
        Start-Sleep -Seconds 1
    }
    if ($null -eq $loadedRules) { throw 'Prometheus did not load and evaluate all eight production rules.' }
    $loadedRules | ConvertTo-Json -Depth 16 | Set-Content (Join-Path $resultsDirectory 'alerts-runtime.json')

    $apiAssembly = Join-Path $repositoryRoot 'src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll'
    if ((Test-Path -LiteralPath $apiAssembly) -eq $false) {
        throw 'Build the Release API before running the isolated alert runtime check.'
    }
    $apiPort = Get-FreeLoopbackPort
    $apiAddress = "http://127.0.0.1:$apiPort"
    $apiKey = 'isolated-alert-test-key-0123456789ABCDEF'
    $apiStart = [System.Diagnostics.ProcessStartInfo]::new()
    $apiStart.FileName = 'dotnet'
    $apiStart.WorkingDirectory = $repositoryRoot
    $apiStart.UseShellExecute = $false
    $apiStart.CreateNoWindow = $true
    $apiStart.RedirectStandardOutput = $true
    $apiStart.RedirectStandardError = $true
    $apiStart.ArgumentList.Add($apiAssembly)
    $apiStart.Environment['urls'] = $apiAddress
    $apiStart.Environment['ASPNETCORE_URLS'] = $apiAddress
    $apiStart.Environment['Metrics__PrometheusUrl'] = "http://$binding/"
    $apiStart.Environment['Backend__Enabled'] = 'false'
    $apiStart.Environment['Collector__Enabled'] = 'false'
    $apiStart.Environment['History__Enabled'] = 'false'
    $apiStart.Environment['Access__ApiKey'] = $apiKey
    [void]$apiStart.Environment.Remove('SONOSMONITORING_CONFIG')

    $apiProcess = [System.Diagnostics.Process]::new()
    $apiProcess.StartInfo = $apiStart
    $logLimit = 65536
    $stdoutLogPath = Join-Path $generatedDirectory 'alerts-api.stdout.log'
    $stderrLogPath = Join-Path $generatedDirectory 'alerts-api.stderr.log'
    [System.IO.File]::WriteAllText($stdoutLogPath, '')
    [System.IO.File]::WriteAllText($stderrLogPath, '')
    $apiStarted = $false
    $stdoutTask = $null
    $stderrTask = $null
    try {
        if ($apiProcess.Start() -eq $false) { throw 'Failed to start the isolated Release API process.' }
        $apiStarted = $true
        $stdoutTask = $apiProcess.StandardOutput.ReadToEndAsync()
        $stderrTask = $apiProcess.StandardError.ReadToEndAsync()
        $httpClient = [System.Net.Http.HttpClient]::new()
        $httpClient.Timeout = [TimeSpan]::FromSeconds(5)
        try {
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
            $healthyResponse = $null
            while ([DateTimeOffset]::UtcNow -lt $deadline -and $apiProcess.HasExited -eq $false) {
                try {
                    $candidate = Invoke-AlertsRequest -Client $httpClient -Uri "$apiAddress/api/alerts" -ApiKey $apiKey
                    if ($candidate.StatusCode -eq 200) {
                        $candidateJson = $candidate.Content | ConvertFrom-Json
                        $candidateMessages = @($candidateJson.messages)
                        $unavailable = @($candidateMessages | Where-Object {
                            $_.ruleName -eq 'SonosApiUnavailable' -and $_.state -in @('pending', 'firing')
                        })
                        if ($unavailable.Count -gt 0) {
                            $activeAlert = $unavailable[0]
                            if ($null -eq $activeAlert.activeAtUtc -or $null -eq $activeAlert.evaluatedAtUtc -or
                                $activeAlert.evaluationDurationSeconds -lt 0) {
                                throw 'The active API message omitted its validated Prometheus timing fields.'
                            }
                            $healthyResponse = $candidate
                            break
                        }
                    }
                }
                catch {
                    # Prometheus may still be completing its first scrape and rule evaluation.
                }
                Start-Sleep -Milliseconds 500
            }
            if ($null -eq $healthyResponse) {
                throw 'The API did not return the pending or firing SonosApiUnavailable rule message.'
            }
            if ($healthyResponse.Content -match '"annotations"\s*:' -or
                $healthyResponse.Content.Contains('SonosMonitoring metrics endpoint is unavailable.', [StringComparison]::Ordinal) -or
                $healthyResponse.Content.Contains('runbook_url', [StringComparison]::Ordinal)) {
                throw 'The alerts API exposed Prometheus annotation text.'
            }

            $deniedResponse = Invoke-AlertsRequest -Client $httpClient -Uri "$apiAddress/api/alerts"
            if ($deniedResponse.StatusCode -ne 403) {
                throw "The unauthenticated alerts API request returned HTTP $($deniedResponse.StatusCode), expected 403."
            }

            $apiResponsePath = Join-Path $resultsDirectory 'alerts-api-runtime.json'
            [System.IO.File]::WriteAllText($apiResponsePath, $healthyResponse.Content)
            Write-Output 'Prometheus rules evaluated; the isolated API returned its active internal message and denied unauthenticated access.'
        }
        finally {
            $httpClient.Dispose()
        }
    }
    finally {
        try {
            if ($apiStarted -and $apiProcess.HasExited -eq $false) {
                try {
                    $apiProcess.Kill($true)
                }
                catch {
                    if ($apiProcess.HasExited -eq $false) { throw }
                }
                if ($apiProcess.WaitForExit(5000) -eq $false) {
                    throw 'The isolated Release API process did not exit after bounded process-tree cleanup.'
                }
            }
            if ($apiStarted -and $apiProcess.HasExited) {
                $apiProcess.WaitForExit()
                if ($null -ne $stdoutTask -and $stdoutTask.Wait(2000)) {
                    $stdoutText = $stdoutTask.GetAwaiter().GetResult()
                    [System.IO.File]::WriteAllText($stdoutLogPath, $stdoutText.Substring(0, [Math]::Min($stdoutText.Length, $logLimit)))
                }
                if ($null -ne $stderrTask -and $stderrTask.Wait(2000)) {
                    $stderrText = $stderrTask.GetAwaiter().GetResult()
                    [System.IO.File]::WriteAllText($stderrLogPath, $stderrText.Substring(0, [Math]::Min($stderrText.Length, $logLimit)))
                }
            }
        }
        finally {
            $apiProcess.Dispose()
        }
    }
}
finally {
    & docker rm --force $containerName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not remove the isolated test container $containerName." }
}
