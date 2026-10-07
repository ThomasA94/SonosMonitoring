[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = 'sonosmonitoring-web-smoke-' + [Guid]::NewGuid().ToString('N')
$reportDirectory = Join-Path $repositoryRoot 'artifacts/test-results'
New-Item -ItemType Directory -Force $reportDirectory | Out-Null
try {
    & docker run --detach --name $containerName --network none sonosmonitoring-web:local | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The isolated web image could not start.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker exec $containerName bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080' 2>$null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if ($ready -eq $false) { throw 'Web listener did not become ready.' }
    $paths = @('/', '/app.css', '/app.js', '/_content/MudBlazor/MudBlazor.min.css', '/_framework/blazor.web.js')
    $evidence = @()
    foreach ($resource in $paths) {
        # Paths are fixed test constants; no user input is interpolated into the shell program.
        $request = 'exec 3<>/dev/tcp/127.0.0.1/8080; printf "GET ' + $resource + ' HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3; cat <&3'
        $response = & docker exec $containerName bash -c $request
        if ($LASTEXITCODE -ne 0 -or ($response -join "`n").StartsWith('HTTP/1.1 200') -eq $false) {
            throw "Web image resource failed: $resource"
        }
        $evidence += "$resource : 200"
    }
    $runtimeUser = & docker inspect --format '{{.Config.User}}' $containerName
    if ($runtimeUser -ne 'app') { throw 'Web image must run as app.' }
    $evidence += 'runtime user: app; network: none; no backend or Sonos access'
    $evidence | Set-Content -LiteralPath (Join-Path $reportDirectory 'web-container-smoke.log') -Encoding utf8
    $evidence | Write-Output
}
finally {
    & docker logs $containerName 2>&1 | Set-Content -LiteralPath (Join-Path $reportDirectory 'web-container-host.log') -Encoding utf8
    & docker rm --force $containerName | Out-Null
}
