[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ScriptPath,

    [string[]]$ScriptArguments = @()
)

$ErrorActionPreference = 'Stop'
# Resolve the supported runtime before the caller changes files, builds, or starts containers.
$powerShell = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $powerShell) {
    throw 'PowerShell 7.3 or newer is required. Install it and ensure pwsh is on PATH, then run this script again.'
}

& $powerShell.Source -NoLogo -NoProfile -NonInteractive -Command 'if ($PSVersionTable.PSVersion -lt [version]::new(7, 3)) { exit 1 }'
if ($LASTEXITCODE -ne 0) {
    throw 'The pwsh executable on PATH must be PowerShell 7.3 or newer.'
}

# Callers supply explicit named arguments and present switches, never a native hashtable splat.
& $powerShell.Source -NoLogo -NoProfile -NonInteractive -File $ScriptPath @ScriptArguments
if ($LASTEXITCODE -ne 0) {
    throw "The PowerShell 7 script failed with exit code $LASTEXITCODE."
}
