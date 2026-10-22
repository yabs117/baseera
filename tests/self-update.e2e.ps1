param([string]$PreviousExecutable)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot)
$project = [xml](Get-Content -Raw src/Baseera.App/Baseera.App.csproj)
$version = [string]$project.Project.PropertyGroup.Version
$channel = [string]$project.Project.PropertyGroup.InformationalVersion
if ([string]::IsNullOrWhiteSpace($channel)) { $channel = $version }
$newSource = (Resolve-Path ('artifacts/Baseera-v' + $version + '-Windows-x64/Baseera.exe')).Path
$source = if ($PreviousExecutable) { (Resolve-Path -LiteralPath $PreviousExecutable).Path } else { $newSource }
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet publish tests/Fixtures/EarlyExit/EarlyExit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false "-p:Version=$version" "-p:InformationalVersion=$channel" -o .local/e2e-fixture --nologo
if ($LASTEXITCODE -ne 0) { throw 'Could not build early-exit fixture' }
$earlyExit = (Resolve-Path '.local/e2e-fixture/Baseera.UpdateFixture.exe').Path
$testRoot = Join-Path (Get-Location) ('.local/update-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Hash([string]$file) {
    $stream = [IO.File]::OpenRead($file); $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hasher.Dispose() }
}
function Wait-Until([scriptblock]$condition, [int]$seconds = 60) {
    $end = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $end) { if (& $condition) { return }; Start-Sleep -Milliseconds 200 }
    throw 'Timed out waiting for updater test state'
}
foreach ($scenario in @('replace', 'rollback')) {
    $caseRoot = Join-Path $testRoot $scenario
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    $target = Join-Path $caseRoot 'My Baseera.exe'
    Copy-Item -LiteralPath $source -Destination $target
    $env:BASEERA_DATA_DIR = Join-Path $caseRoot 'data'
    New-Item -ItemType Directory -Path $env:BASEERA_DATA_DIR | Out-Null
    $settingsFile = Join-Path $env:BASEERA_DATA_DIR 'settings.json'
    [IO.File]::WriteAllText($settingsFile, '{"Language":"en","Onboarded":true,"WelcomeSeen":true,"CheckBaseeraUpdates":false,"GeminiModel":"e2e-retain-settings"}')
    $secretFile = Join-Path $env:BASEERA_DATA_DIR 'secrets.bin'
    [IO.File]::WriteAllBytes($secretFile, [byte[]](19, 88, 34, 201))
    $settingsHash = Hash $settingsFile; $secretHash = Hash $secretFile; $oldHash = Hash $target
    $folder = Join-Path $caseRoot ('.baseera-update-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $folder 'helper.exe')
    $payload = Join-Path $folder 'payload.exe'
    $bytes = [IO.File]::ReadAllBytes($newSource)
    if ($scenario -eq 'replace') {
        # DOS stub message is unused on Windows; change the hash without disturbing the bundle.
        $bytes[80] = $bytes[80] -bxor 1
    } else {
        # A valid app that exits before the window-loaded acknowledgement.
        $bytes = [IO.File]::ReadAllBytes($earlyExit)
    }
    [IO.File]::WriteAllBytes($payload, $bytes); $newHash = Hash $payload
    $parent = $null; $helper = $null
    try {
        $parent = Start-Process -FilePath $target -WindowStyle Hidden -PassThru
        Wait-Until { $parent.Refresh(); if ($parent.HasExited) { throw 'Test app exited before update' }; $parent.MainWindowHandle -ne 0 }
        $plan = @{
            Target = $target; OldHash = $oldHash; ParentId = $parent.Id; ParentStarted = $parent.StartTime.ToUniversalTime().Ticks
            Release = @{ Tag = ('v' + $channel); Version = $version; Notes = 'Isolated updater test'; Download = ('https://github.com/yabs117/baseera/releases/download/v' + $channel + '/Baseera-v' + $version + '-Windows-x64.exe'); Size = $bytes.Length; Sha256 = $newHash }
        }
        $planFile = Join-Path $folder 'plan.json'
        [IO.File]::WriteAllText($planFile, ($plan | ConvertTo-Json -Depth 5))
        $helperArgs = '--apply-update "' + $planFile + '"'
        $helper = Start-Process -FilePath (Join-Path $folder 'helper.exe') -ArgumentList $helperArgs -WindowStyle Hidden -PassThru
        Wait-Until { if ($helper.HasExited) { throw 'Helper exited before readiness' }; Test-Path -LiteralPath (Join-Path $folder 'ready') }
        if (-not $parent.CloseMainWindow()) { throw 'Could not close isolated test parent' }
        if (-not $parent.WaitForExit(15000)) { throw 'Test parent did not exit' }
        if (-not $helper.WaitForExit(90000)) { throw 'Helper did not finish' }
        $expected = if ($scenario -eq 'replace') { $newHash } else { $oldHash }
        if ((Hash $target) -ne $expected) { throw 'Executable bytes do not match expected transaction result' }
        if ((Hash $settingsFile) -ne $settingsHash -or (Hash $secretFile) -ne $secretHash) { throw 'Update altered preferences or secret storage' }
        $expectedExit = if ($scenario -eq 'replace') { 0 } else { 1 }
        if ($helper.ExitCode -ne $expectedExit) { throw ('Unexpected helper exit: ' + $helper.ExitCode) }
        Wait-Until { @(Get-Process -Name 'My Baseera' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }).Count -eq 1 }
        Write-Output ('PASS: actual standalone ' + $scenario + ', same custom filename, restart, settings and secret bytes retained')
        Wait-Until { -not (Test-Path -LiteralPath $folder) }
        Write-Output ('PASS: ' + $scenario + ' transaction cleaned after restart acknowledgement')
    } finally {
        # Stop only processes whose verified executable paths are inside this test case.
        foreach ($process in @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target -or $_.Path -eq (Join-Path $folder 'helper.exe') })) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    }
    $checkedRoot = [IO.Path]::GetFullPath($caseRoot)
    if (-not $checkedRoot.StartsWith([IO.Path]::GetFullPath($testRoot) + [IO.Path]::DirectorySeparatorChar)) { throw 'Invalid cleanup root' }
    for ($attempt = 0; ; $attempt++) {
        try { Remove-Item -LiteralPath $checkedRoot -Recurse -Force; break }
        catch { if ($attempt -ge 20) { throw }; Start-Sleep -Milliseconds 250 }
    }
}
[IO.File]::WriteAllText((Join-Path $testRoot 'result.txt'), 'PASS: real same-path replacement, restart handshake, rollback after early startup failure, settings retained, staging cleanup')
Write-Output ('Evidence: ' + (Join-Path $testRoot 'result.txt'))
