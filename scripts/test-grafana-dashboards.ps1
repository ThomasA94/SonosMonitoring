[CmdletBinding()]
param(
    [string]$DashboardDirectory,
    [string]$CatalogPath,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DashboardDirectory)) {
    $DashboardDirectory = Join-Path $repositoryRoot 'infra/grafana/dashboards'
}
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $repositoryRoot 'contracts/metrics.json'
}
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
$metricNames = @($catalog.Name)
$datasourceText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'infra/grafana/provisioning/datasources/prometheus.yaml') -Raw
$datasourceMatch = [regex]::Match($datasourceText, '(?m)^\s+uid:\s*(\S+)\s*$')
if ($datasourceMatch.Success -eq $false -or $datasourceText -notmatch '(?m)^\s+type:\s*prometheus\s*$') {
    throw 'The provisioned Prometheus datasource must declare one stable UID.'
}
$datasourceUid = $datasourceMatch.Groups[1].Value
$issues = [System.Collections.Generic.List[string]]::new()
$summaries = [System.Collections.Generic.List[object]]::new()
$dashboardUids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$expressionCount = 0

# Require polling for the project's Docker bind mounts; watcher-only updates can be lost.
$providerText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'infra/grafana/provisioning/dashboards/sonosmonitoring.yaml') -Raw
$pollInterval = [regex]::Match($providerText, '(?m)^\s+updateIntervalSeconds:\s*(\d+)\s*$')
if ($pollInterval.Success -eq $false -or [int]$pollInterval.Groups[1].Value -le 10) {
    $issues.Add('Dashboard provisioning must poll at an interval greater than 10 seconds for Docker bind mounts.')
}

function Add-Issue {
    <# Collects all contract violations so one run reports every broken dashboard. #>
    param([string]$Context, [string]$Message)
    $issues.Add("${Context}: $Message")
}

function Get-Panels {
    <# Includes panels nested inside collapsed Grafana rows. #>
    param([object[]]$Panels)
    foreach ($panel in $Panels) {
        $panel
        if ($null -ne $panel.panels) {
            Get-Panels -Panels $panel.panels
        }
    }
}

function Test-Datasource {
    <# Prevents dashboard JSON from silently querying a different datasource. #>
    param([object]$Datasource, [string]$Context)
    if ($null -eq $Datasource) {
        Add-Issue $Context 'A query datasource is missing.'
    }
    elseif ($Datasource.type -cne 'prometheus' -or $Datasource.uid -cne $datasourceUid) {
        Add-Issue $Context "Use the provisioned Prometheus datasource '$datasourceUid'."
    }
}

function Test-Expression {
    <# Checks application metric names without claiming to parse PromQL syntax. #>
    param([string]$Expression, [string]$Context)
    if ([string]::IsNullOrWhiteSpace($Expression)) {
        Add-Issue $Context 'A query expression is empty.'
        return
    }
    $script:expressionCount++
    foreach ($name in ([regex]::Matches($Expression, '\bsonos_[a-zA-Z0-9_]+\b') | ForEach-Object Value | Sort-Object -Unique)) {
        if ($name -cnotin $metricNames) {
            Add-Issue $Context "Undeclared application metric '$name'."
        }
    }
}

function Test-PanelUnits {
    <# Keeps binary statuses, measured seconds and rates on meaningful field scales. #>
    param([object]$Panel, [string]$Context)
    $units = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($target in @($Panel.targets)) {
        $expression = [string]$target.expr
        # Counts of players or sources are cardinalities, rather than binary status fields.
        if ($expression -match '^\s*(count|sum)\s*(\(|by\b)' -or $Panel.type -eq 'table') {
            continue
        }
        foreach ($name in ([regex]::Matches($expression, '\bsonos_[a-zA-Z0-9_]+\b') | ForEach-Object Value | Sort-Object -Unique)) {
            if ($name -match '_info$') { continue }
            if ($name -match '(_enabled|_success|_fresh|_subscribed|_initial_received|_muted|_link_up)$') {
                # A same-expression freshness mask retains the measured rate's unit.
                if ($name -eq 'sonos_source_observation_fresh' -and $expression -match '\b(i?rate)\s*\(') {
                    continue
                }
                [void]$units.Add('none')
            }
            elseif ($name -match '_seconds$') {
                [void]$units.Add('s')
            }
            elseif ($name -match '_bytes_total$') {
                [void]$units.Add($(if ($expression -match '\b(i?rate)\s*\(') { 'Bps' } else { 'bytes' }))
            }
            elseif ($name -match '_packets_total$' -and $expression -match '\b(i?rate)\s*\(') {
                [void]$units.Add('pps')
            }
            elseif ($name -match '_(errors|dropped)_total$' -and $expression -match '\b(i?rate)\s*\(') {
                [void]$units.Add('ops')
            }
            elseif ($name -eq 'sonos_device_volume') {
                [void]$units.Add('percent')
            }
        }
    }
    $unitOverrides = @($Panel.fieldConfig.overrides | ForEach-Object { $_.properties } | Where-Object { $_.id -eq 'unit' })
    if ($units.Count -gt 1 -and $unitOverrides.Count -eq 0) {
        Add-Issue $Context "Incompatible field units ($($units -join ', ')) require separate panels or explicit field overrides."
    }
    elseif ($units.Count -eq 1 -and $unitOverrides.Count -eq 0) {
        $expected = @($units)[0]
        if ($Panel.fieldConfig.defaults.unit -cne $expected) {
            Add-Issue $Context "Expected field unit '$expected', found '$($Panel.fieldConfig.defaults.unit)'."
        }
    }
}

$files = @(Get-ChildItem -LiteralPath $DashboardDirectory -Filter '*.json' -File | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No Grafana dashboard JSON files found in '$DashboardDirectory'."
}
foreach ($file in $files) {
    $dashboard = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $context = $file.Name
    if ([string]::IsNullOrWhiteSpace($dashboard.uid) -or $dashboardUids.Add($dashboard.uid) -eq $false) {
        Add-Issue $context 'Dashboard UID is missing or duplicated.'
    }
    $panelIds = [System.Collections.Generic.HashSet[int]]::new()
    $panels = @(Get-Panels -Panels $dashboard.panels)
    foreach ($panel in $panels) {
        $panelContext = "$context / panel $($panel.id) ($($panel.title))"
        if ($null -eq $panel.id -or $panel.id -lt 1 -or $panelIds.Add([int]$panel.id) -eq $false) {
            Add-Issue $panelContext 'Panel ID must be a unique positive integer within this dashboard.'
        }
        $targets = @($panel.targets | Where-Object { $null -ne $_ })
        if ($targets.Count -gt 0) {
            Test-Datasource -Datasource $panel.datasource -Context $panelContext
            $referenceIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($target in $targets) {
                $targetContext = "$panelContext / target $($target.refId)"
                if ([string]::IsNullOrWhiteSpace($target.refId) -or $referenceIds.Add($target.refId) -eq $false) {
                    Add-Issue $targetContext 'Target reference ID is missing or duplicated.'
                }
                if ($null -ne $target.datasource) {
                    Test-Datasource -Datasource $target.datasource -Context $targetContext
                }
                Test-Expression -Expression $target.expr -Context $targetContext
                if ($target.legendFormat -match '\{\{\s*device_id\s*\}\}') {
                    Add-Issue $targetContext 'A stable device identifier must not be displayed as a legend name.'
                }
                if ($panel.type -ne 'table' -and $target.expr -match '\bdevice_id\b' -and
                    $target.expr -notmatch '^\s*(count|sum|absent)\s*(\(|by\b)' -and
                    $target.legendFormat -notmatch '\{\{\s*device_name\s*\}\}') {
                    Add-Issue $targetContext 'Device series must display a readable device_name legend.'
                }
                if ($target.legendFormat -match '\{\{\s*device_name\s*\}\}' -and $target.expr -notmatch '\bsonos_device_info\b') {
                    Add-Issue $targetContext 'Readable device legends require the device metadata join.'
                }
            }
            Test-PanelUnits -Panel $panel -Context $panelContext
        }
    }
    $deviceVariables = @($dashboard.templating.list | Where-Object { $_.name -eq 'device' })
    if ($deviceVariables.Count -ne 1) {
        Add-Issue $context 'One readable device selector named device is required.'
    }
    foreach ($variable in @($dashboard.templating.list)) {
        if ($variable.type -ne 'query') { continue }
        $variableContext = "$context / variable $($variable.name)"
        Test-Datasource -Datasource $variable.datasource -Context $variableContext
        $query = $(if ($variable.query -is [string]) { $variable.query } else { $variable.query.query })
        Test-Expression -Expression $query -Context $variableContext
        if ($variable.name -eq 'device' -and ($query -notmatch '\bsonos_device_info\b' -or
            $variable.regex -notmatch 'device_name=[^|]*\(\?<text>' -or
            $variable.regex -notmatch 'device_id=[^|]*\(\?<value>')) {
            Add-Issue $variableContext 'Device selection must expose readable metadata as text and the stable ID as value.'
        }
    }
    $summaries.Add([pscustomobject]@{ File = $file.Name; Uid = $dashboard.uid; Panels = $panels.Count })
}

$resultsDirectory = Join-Path $repositoryRoot 'artifacts/test-results'
New-Item -ItemType Directory -Force $resultsDirectory | Out-Null
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $resultsDirectory 'grafana-dashboard-contracts.json'
}
$report = [pscustomobject]@{
    CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    DashboardCount = $files.Count
    ExpressionCount = $expressionCount
    DatasourceUid = $datasourceUid
    Dashboards = @($summaries)
    Issues = @($issues)
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding utf8
if ($issues.Count -gt 0) {
    throw "Grafana dashboard contracts failed:`n$($issues -join "`n")"
}
Write-Output "Grafana dashboard contracts passed: $($files.Count) dashboards, $expressionCount queries; declared metrics, stable datasource, readable selectors and compatible units."
