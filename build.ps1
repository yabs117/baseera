param([switch]$Test, [switch]$Publish, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.local/dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:BASEERA_DATA_DIR = Join-Path $PSScriptRoot '.local/smoke-data'
& dotnet build src/Baseera.App/Baseera.App.csproj -c Release -p:NuGetAudit=false --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Test) {
    & dotnet run --project tests/Baseera.Tests/Baseera.Tests.csproj -c Release -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($Smoke) {
    $process = Start-Process -FilePath 'src/Baseera.App/bin/Release/net10.0-windows/Baseera.exe' -ArgumentList '--smoke artifacts/screenshots' -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { exit $process.ExitCode }
    Get-Content -LiteralPath 'artifacts/screenshots/smoke-result.txt'
}
if ($Publish) {
    & dotnet publish src/Baseera.App/Baseera.App.csproj -c Release --no-restore -o artifacts/Baseera-win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item -LiteralPath 'README.md', 'LICENSE' -Destination 'artifacts/Baseera-win-x64'
    Compress-Archive -Path 'artifacts/Baseera-win-x64/*' -DestinationPath 'artifacts/Baseera-v0.1.0-win-x64.zip' -Force
    Compress-Archive -Path 'extension/*' -DestinationPath 'artifacts/Baseera-Translate-v0.1.0.zip' -Force
    $hashLines = foreach ($packagePath in @('artifacts/Baseera-v0.1.0-win-x64.zip', 'artifacts/Baseera-Translate-v0.1.0.zip')) {
        $hasher = [System.Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($hasher.ComputeHash([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $packagePath)))).Replace('-', '').ToLowerInvariant() }
        finally { $hasher.Dispose() }
        $digest + '  ' + (Split-Path $packagePath -Leaf)
    }
    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'artifacts/SHA256SUMS.txt'), $hashLines, [Text.Encoding]::ASCII)
}
