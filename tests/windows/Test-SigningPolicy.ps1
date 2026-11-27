$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$identityScript = Join-Path $repo 'packaging/release-identity.ps1'
$preview = & $identityScript -Version '0.4.17' -InformationalVersion '0.4.17-preview'
$stable = & $identityScript -Version '0.4.17' -InformationalVersion '0.4.17'
if ($preview.Channel -ne 'preview' -or $stable.Channel -ne 'stable') { throw 'Preview and Stable signing gates are not distinguished.' }
$invalidIdentity = $false
try { & $identityScript -Version '0.4.17' -InformationalVersion '0.4.18-preview' } catch { $invalidIdentity = $true }
if (!$invalidIdentity) { throw 'Mismatched release identity accepted.' }
$folder = Join-Path $repo ('artifacts/signing-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$file = Join-Path $folder 'Unsigned.exe'
[IO.File]::WriteAllBytes($file, [byte[]](77,90,0,0))
$before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
$script = Join-Path $repo 'packaging/sign-release.ps1'
$rejected = $false
try { & $script -Path $file } catch { $rejected = $_.Exception.Message -like '*Explicit publisher certificate*' }
if (!$rejected) { throw 'Signing did not require explicit certificate selection.' }
$rejected = $false
try { & $script -Path $file -VerifyOnly } catch { if ($_.Exception.Message -notlike '*trusted, timestamped*') { throw }; $rejected = $true }
if (!$rejected) { throw 'Unsigned payload passed the trust gate.' }
$rejected = $false
try { & $script -Path $file -CertificateThumbprint 'not-a-thumbprint' } catch { $rejected = $_.Exception.Message -like '*exact certificate thumbprint*' }
if (!$rejected -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) -ne $before) { throw 'Rejected signing modified the payload.' }
Write-Output 'PASS: Preview/Stable identity gates, mismatched identity rejected, missing/invalid certificate selection rejected, unsigned payload rejected, payload bytes unchanged. No private key used.'
