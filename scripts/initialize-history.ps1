<#
.SYNOPSIS
Creates observation-history schema version 1 in an explicitly named PostgreSQL database.
.DESCRIPTION
Fails if the schema already exists. This is creation, never a migration or data reset.
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
if ((Test-Path -LiteralPath $apiAssembly -PathType Leaf) -eq $false) { throw 'Build the Release API before schema creation.' }
$schemaPath = Join-Path ([System.IO.Path]::GetTempPath()) "sonos-history-$([Guid]::NewGuid().ToString('N')).sql"
try {
    & dotnet $apiAssembly --write-observation-history-schema $schemaPath
    if ($LASTEXITCODE -ne 0) { throw 'History schema generation failed.' }
    Get-Content -LiteralPath $schemaPath -Raw | & docker exec -i $ContainerName psql -X -v ON_ERROR_STOP=1 -U $User -d $Database -f -
    if ($LASTEXITCODE -ne 0) { throw 'History schema creation failed. Existing schemas require explicit version inspection, not reinitialization.' }
    Write-Output 'Observation-history schema version 1 created.'
}
finally {
    if (Test-Path -LiteralPath $schemaPath -PathType Leaf) { Remove-Item -LiteralPath $schemaPath -Force }
}
