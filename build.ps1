param([switch]$Test, [switch]$Publish, [switch]$Smoke, [string]$SigningThumbprint)
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
    $project = [xml](Get-Content -LiteralPath 'src/Baseera.App/Baseera.App.csproj')
    $version = [string]$project.Project.PropertyGroup.Version
    $releaseIdentity = & ./packaging/release-identity.ps1 -Version $version -InformationalVersion ([string]$project.Project.PropertyGroup.InformationalVersion)
    $desktopStage = "artifacts/Baseera-v$version-Windows-x64"
    $compatibilityExe = "artifacts/Baseera-v$version-Windows-x64.exe"
    & dotnet publish src/Baseera.App/Baseera.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:NuGetAudit=false -o $desktopStage --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($SigningThumbprint) { & ./packaging/sign-release.ps1 -Path "$desktopStage/Baseera.exe" -CertificateThumbprint $SigningThumbprint }
    if ($releaseIdentity.Channel -eq 'stable') { & ./packaging/sign-release.ps1 -Path "$desktopStage/Baseera.exe" -VerifyOnly -CertificateThumbprint $SigningThumbprint }
    Copy-Item -LiteralPath 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'packaging/desktop/START-HERE.html', 'src/Baseera.App/Assets/Fonts/OFL.txt', 'src/Baseera.App/Assets/Fonts/Noto-OFL.txt' -Destination $desktopStage
    Copy-Item -LiteralPath 'src/Baseera.App/Assets/Licenses/PdfPig-APACHE-2.0.txt', 'src/Baseera.App/Assets/Licenses/PdfPig-NOTICES.txt' -Destination $desktopStage
    $browserStage = "artifacts/Baseera-v$version-Browser-Extension"
    New-Item -ItemType Directory -Force -Path "$browserStage/extension" | Out-Null
    Copy-Item -LiteralPath 'packaging/browser/START-HERE.html', 'LICENSE', 'THIRD-PARTY-NOTICES.md' -Destination $browserStage
    Copy-Item -LiteralPath 'extension/assets' -Destination "$browserStage/extension" -Recurse -Force
    Get-ChildItem -LiteralPath 'extension' -File | Where-Object { $_.Extension -in '.mjs', '.css', '.html', '.json' -and $_.Name -ne 'package.json' } | Copy-Item -Destination "$browserStage/extension"
    Compress-Archive -Path "$desktopStage/*" -DestinationPath 'artifacts/Baseera.zip' -Force
    Compress-Archive -Path "$browserStage/*" -DestinationPath 'artifacts/Baseera-Browser-Extension.zip' -Force
    Copy-Item -LiteralPath "$desktopStage/Baseera.exe" -Destination 'artifacts/Baseera.exe'
    # Transition asset required by the updater already installed in 0.4.1–0.4.13.
    Copy-Item -LiteralPath 'artifacts/Baseera.exe' -Destination $compatibilityExe
    if ($Smoke) {
        $process = Start-Process -FilePath "$desktopStage/Baseera.exe" -ArgumentList '--smoke artifacts/packaged-screenshots' -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { exit $process.ExitCode }
    }
    $hashLines = foreach ($packagePath in @('artifacts/Baseera.zip', 'artifacts/Baseera.exe', 'artifacts/Baseera-Browser-Extension.zip', $compatibilityExe)) {
        $hasher = [System.Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($hasher.ComputeHash([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $packagePath)))).Replace('-', '').ToLowerInvariant() }
        finally { $hasher.Dispose() }
        $digest + '  ' + (Split-Path $packagePath -Leaf)
    }
    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'artifacts/SHA256SUMS.txt'), $hashLines, [Text.Encoding]::ASCII)
}
