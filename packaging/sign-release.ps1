param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$CertificateThumbprint,
    [uri]$TimestampUrl = 'https://timestamp.digicert.com',
    [string]$SignToolPath,
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$target = (Resolve-Path -LiteralPath $Path).Path
if ([IO.Path]::GetExtension($target) -ne '.exe') { throw 'Only a published EXE may be signed.' }
if ($CertificateThumbprint) {
    $CertificateThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    if ($CertificateThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Supply the exact certificate thumbprint.' }
}
if (!$VerifyOnly) {
    if (!$CertificateThumbprint) { throw 'Explicit publisher certificate selection is required. No certificate is selected automatically.' }
    if ($TimestampUrl.Scheme -ne 'https' -or $TimestampUrl.UserInfo -or !$TimestampUrl.IsDefaultPort) { throw 'An HTTPS RFC3161 timestamp URL is required.' }
    $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (!$cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date) -or
        !($cert.EnhancedKeyUsageList | Where-Object { $_.ObjectId.Value -eq '1.3.6.1.5.5.7.3.3' })) { throw 'The selected certificate is not usable for code signing.' }
    if (!$SignToolPath) {
        $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($command) { $SignToolPath = $command.Source }
        else {
            $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
            $SignToolPath = Get-ChildItem -LiteralPath $sdkBin -Directory | Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
    }
    if (!$SignToolPath -or !(Test-Path -LiteralPath $SignToolPath)) { throw 'Install the Windows SDK SignTool or supply its path.' }
    & $SignToolPath sign /sha1 $CertificateThumbprint /s My /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $target
    if ($LASTEXITCODE -ne 0) { throw 'Code signing failed; do not publish this build.' }
    & $SignToolPath verify /pa /all /v $target
    if ($LASTEXITCODE -ne 0) { throw 'SignTool verification failed.' }
}
if (!(Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
    Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Security/Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
}
$signature = Get-AuthenticodeSignature -LiteralPath $target
if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate -or !$signature.TimeStamperCertificate) { throw 'A trusted, timestamped Authenticode signature is required.' }
if ($CertificateThumbprint -and $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) { throw 'The signer does not match the explicitly selected publisher.' }
Write-Output ('Verified Authenticode signer: ' + $signature.SignerCertificate.Thumbprint)
