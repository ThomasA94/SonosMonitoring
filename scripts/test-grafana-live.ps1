[CmdletBinding()]
param([string]$GrafanaUrl = 'http://127.0.0.1:48300')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$address = [Uri]$GrafanaUrl
if ($address.IsLoopback -eq $false) { throw 'Live dashboard acceptance must use an explicitly selected loopback Grafana.' }
$username = [Environment]::GetEnvironmentVariable('GRAFANA_ADMIN_USER', 'Process')
$password = [Environment]::GetEnvironmentVariable('GRAFANA_ADMIN_PASSWORD', 'Process')
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
    throw 'Set GRAFANA_ADMIN_USER and GRAFANA_ADMIN_PASSWORD in the process environment.'
}
$headers = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($username + ':' + $password)) }
$base = $GrafanaUrl.TrimEnd('/')
$proxy = "$base/api/datasources/proxy/uid/sonosmonitoring-prometheus"
$results = [System.Collections.Generic.List[object]]::new()
$queries = [System.Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)

function Expand-Expression {
    <# Substitutes bounded all-device filters for testing actual dashboard expressions. #>
    param([string]$Expression)
    return $Expression.Replace('${device:regex}', '.*').Replace('${interface:regex}', '.*').Replace('${source:regex}', '.*')
}

function Invoke-PrometheusQuery {
    <# Checks the actual provisioned datasource, without recording raw names or keys. #>
    param([string]$Expression, [switch]$Range, [long]$Start = 0, [long]$End = 0)
    $path = '/api/v1/query?query=' + [Uri]::EscapeDataString($Expression)
    if ($Range) {
        if ($End -eq 0) { $End = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() }
        if ($Start -eq 0) { $Start = $End - 300 }
        $path = '/api/v1/query_range?query=' + [Uri]::EscapeDataString($Expression) + "&start=$Start&end=$End&step=15"
    }
    $response = Invoke-RestMethod ($proxy + $path) -Headers $headers -TimeoutSec 20
    if ($response.status -cne 'success') { throw 'Grafana datasource returned an unsuccessful Prometheus query.' }
    return $response.data
}

function Test-NamedHistory {
    <# Ensures a metadata join never removes, duplicates or changes already measured counter history. #>
    param([string]$RawExpression, [string]$NamedExpression, [long]$Start, [long]$End)
    $raw = Invoke-PrometheusQuery $RawExpression -Range -Start $Start -End $End
    $named = Invoke-PrometheusQuery $NamedExpression -Range -Start $Start -End $End
    if ($raw.result.Count -ne $named.result.Count) { throw 'Readable-name joins changed the number of historical counter series.' }
    $fallbackCount = 0
    foreach ($series in $raw.result) {
        $matching = @($named.result | Where-Object {
            $_.metric.device_id -ceq $series.metric.device_id -and $_.metric.interface -ceq $series.metric.interface
        })
        if ($matching.Count -ne 1 -or
            ($matching[0].values | ConvertTo-Json -Depth 6 -Compress) -cne ($series.values | ConvertTo-Json -Depth 6 -Compress)) {
            throw 'Readable-name joins changed historical counter samples.'
        }
        if ([string]::IsNullOrWhiteSpace($matching[0].metric.device_name)) { throw 'Historical series lost their readable fallback.' }
        if ($matching[0].metric.device_name.StartsWith('Unbekannter Player', [StringComparison]::Ordinal)) { $fallbackCount++ }
    }
    return [pscustomobject]@{ HasData = $raw.result.Count -gt 0; Series = $raw.result.Count; SamplesEqual = $true; FallbackNames = $fallbackCount }
}

foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'infra/grafana/dashboards') -Filter '*.json' -File) {
    $expected = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $loaded = (Invoke-RestMethod "$base/api/dashboards/uid/$($expected.uid)" -Headers $headers -TimeoutSec 15).dashboard
    if ($loaded.title -cne $expected.title -or $loaded.panels.Count -ne $expected.panels.Count) {
        throw "Provisioned dashboard $($expected.uid) is stale."
    }
    foreach ($panel in $expected.panels) {
        $actualPanel = $loaded.panels | Where-Object id -eq $panel.id
        if ($actualPanel.title -cne $panel.title -or
            ($actualPanel.targets.expr | ConvertTo-Json -Compress) -cne ($panel.targets.expr | ConvertTo-Json -Compress)) {
            throw "Provisioned panel $($expected.uid)/$($panel.id) differs from its source."
        }
        foreach ($target in $panel.targets) {
            $expression = Expand-Expression $target.expr
            $queries[$expression] = $queries.ContainsKey($expression) -and $queries[$expression] -or $target.range -eq $true
        }
    }
    $results.Add([pscustomobject]@{ Dashboard = $expected.uid; Panels = $expected.panels.Count })
}

$instantChecks = 0
$rangeChecks = 0
$namedSeries = 0
$emptyQueries = 0
foreach ($query in $queries.GetEnumerator()) {
    $data = Invoke-PrometheusQuery $query.Key
    $instantChecks++
    if ($data.result.Count -eq 0) { $emptyQueries++ }
    if ($query.Key -match 'group_left\(device_name') {
        foreach ($series in $data.result) {
            if ([string]::IsNullOrWhiteSpace($series.metric.device_name)) { throw 'A joined device series is missing its readable name.' }
            $namedSeries++
        }
    }
    if ($query.Value) {
        [void](Invoke-PrometheusQuery $query.Key -Range)
        $rangeChecks++
    }
}
$metadata = Invoke-PrometheusQuery 'sonos_device_info'
if ($metadata.result.Count -eq 0 -or $namedSeries -eq 0) { throw 'Current player metadata and named device measurements must be present.' }
foreach ($series in $metadata.result) {
    if ([string]::IsNullOrWhiteSpace($series.metric.device_name) -or $series.metric.device_name -ceq $series.metric.device_id) {
        throw 'Player metadata must provide a readable name independent of its key.'
    }
}
$network = Get-Content -LiteralPath (Join-Path $repositoryRoot 'infra/grafana/dashboards/sonosmonitoring-network.json') -Raw | ConvertFrom-Json
$receiveTarget = @($network.panels.targets | Where-Object { $_.expr -match 'rate\(sonos_interface_receive_bytes_total' })[0]
$namedReceive = Expand-Expression $receiveTarget.expr
$rawReceive = '(rate(sonos_interface_receive_bytes_total{device_id=~".*",interface=~".*"}[2m]) and on(device_id) (sonos_source_observation_fresh{device_id=~".*",source="ifconfig"} == 1))'
$historyEnd = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - 15
$currentHistory = Test-NamedHistory $rawReceive $namedReceive ($historyEnd - 7200) $historyEnd
$olderHistory = Test-NamedHistory $rawReceive $namedReceive ($historyEnd - 1500) ($historyEnd - 1200)
# A deliberately unmatched metadata key verifies fallback without depending on deployment timing.
$missingMetadataReceive = $namedReceive.Replace('sonos_device_info{device_id=~".*"}', 'sonos_device_info{device_id="__unresolved_metadata_test__"}')
if ($missingMetadataReceive -ceq $namedReceive) { throw 'The dashboard metadata selector could not be isolated for the history regression check.' }
$missingMetadataHistory = Test-NamedHistory $rawReceive $missingMetadataReceive ($historyEnd - 7200) $historyEnd
if ($missingMetadataHistory.FallbackNames -ne $missingMetadataHistory.Series) { throw 'Missing metadata did not retain all counter series with explicit fallback names.' }
$report = [pscustomobject]@{
    CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Dashboards = @($results)
    DeviceCount = $metadata.result.Count
    DistinctInstantQueries = $instantChecks
    DistinctRangeQueries = $rangeChecks
    ReadableSeriesChecked = $namedSeries
    EmptyInstantQueries = $emptyQueries
    CurrentWindowHistory = $currentHistory
    EarlierWindowHistory = $olderHistory
    MissingMetadataHistory = $missingMetadataHistory
    Note = 'Missing optional GENA notifications or unsupported diagnostics remain absent; no raw device metadata is stored in this report.'
}
$output = Join-Path $repositoryRoot 'artifacts/test-results/grafana-live.json'
New-Item -ItemType Directory -Force (Split-Path -Parent $output) | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $output -Encoding utf8
Write-Output "Live Grafana acceptance passed: $($results.Count) dashboards, $instantChecks instant and $rangeChecks range queries, $($metadata.result.Count) named players."
