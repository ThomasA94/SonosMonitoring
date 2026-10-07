<#
.SYNOPSIS
Creates media coordination schema version 1 in an explicitly named PostgreSQL database.
.DESCRIPTION
Fails if the sonos_media namespace already exists. This is first installation, never a
migration or data reset. The initial durable media control grant is disabled.
Build the Release API first. Authentication uses the named container's local PostgreSQL setup.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$')][string]$ContainerName,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z_][A-Za-z0-9_]{0,62}$')][string]$Database,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z_][A-Za-z0-9_]{0,62}$')][string]$User
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiAssembly = Join-Path $repositoryRoot 'src/SonosMonitoring.Api/bin/Release/net10.0/SonosMonitoring.Api.dll'
if ((Test-Path -LiteralPath $apiAssembly -PathType Leaf) -eq $false) { throw 'Build the Release API before media schema creation.' }

$present = & docker exec $ContainerName psql -X -A -t -v ON_ERROR_STOP=1 -U $User -d $Database -c "SELECT to_regnamespace('sonos_media') IS NOT NULL"
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the media schema namespace.' }
if (($present -join '').Trim() -cne 'f') { throw 'The sonos_media namespace exists. Refusing to initialize or migrate it.' }

$schemaPath = Join-Path ([System.IO.Path]::GetTempPath()) "sonos-media-$([Guid]::NewGuid().ToString('N')).sql"
try {
    & dotnet $apiAssembly --write-media-control-schema $schemaPath
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $schemaPath -PathType Leaf) -eq $false) { throw 'Media schema generation failed.' }
    Get-Content -LiteralPath $schemaPath -Raw | & docker exec -i $ContainerName psql -X -v ON_ERROR_STOP=1 -U $User -d $Database -f -
    if ($LASTEXITCODE -ne 0) { throw 'Media schema creation failed. Inspect the destination; do not reinitialize an existing namespace.' }
    Write-Output 'Media coordination schema version 1 created with control disabled.'
}
finally {
    if (Test-Path -LiteralPath $schemaPath -PathType Leaf) { Remove-Item -LiteralPath $schemaPath -Force }
}
