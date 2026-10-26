param([Parameter(Mandatory=$true)][string]$Version, [Parameter(Mandatory=$true)][string]$InformationalVersion)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'A numeric three-part assembly version is required.' }
$expected = '^' + [regex]::Escape($Version) + '(-preview(\.\d+)?)?$'
if ($InformationalVersion -notmatch $expected) { throw 'Release identity must match the assembly version and supported channel syntax.' }
[pscustomobject]@{ Version = $Version; Tag = $InformationalVersion; Channel = $(if ($InformationalVersion -match '-preview') { 'preview' } else { 'stable' }) }
