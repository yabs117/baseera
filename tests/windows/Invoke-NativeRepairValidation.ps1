param([switch]$RunNativeRepairs)
$ErrorActionPreference = 'Stop'
if (!$RunNativeRepairs -or $env:BASEERA_DISPOSABLE_VM_TEST -ne '1' -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Native servicing tests are restricted to an explicitly dispatched disposable GitHub-hosted Windows guest. Never run on a personal machine.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'The disposable guest must already be elevated; this script never bypasses UAC.' }
$output = Join-Path $PSScriptRoot '../../artifacts/native-vm-evidence'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$steps = @(
    @{Id='dism-check-before';Exe='Dism.exe';Args=@('/Online','/Cleanup-Image','/CheckHealth','/NoRestart')},
    @{Id='sfc-verify-before';Exe='sfc.exe';Args=@('/verifyonly')},
    @{Id='sfc-repair';Exe='sfc.exe';Args=@('/scannow')},
    @{Id='dism-repair';Exe='Dism.exe';Args=@('/Online','/Cleanup-Image','/RestoreHealth','/NoRestart')},
    @{Id='sfc-verify-after';Exe='sfc.exe';Args=@('/verifyonly')},
    @{Id='dism-scan-after';Exe='Dism.exe';Args=@('/Online','/Cleanup-Image','/ScanHealth','/NoRestart')}
)
$records = [Collections.Generic.List[object]]::new()
$started = [DateTimeOffset]::UtcNow
try {
    foreach ($step in $steps) {
        $exe = Join-Path ([Environment]::SystemDirectory) $step.Exe
        $begin = [DateTimeOffset]::UtcNow
        Write-Output "VM step: $($step.Id)"
        $info = [Diagnostics.ProcessStartInfo]::new($exe)
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in $step.Args) { $info.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($info)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        # Never kill Windows servicing. The platform disposes only this disposable guest.
        if (!$process.WaitForExit(20 * 60 * 1000)) {
            $records.Add(@{id=$step.Id;started_utc=$begin;finished_utc=[DateTimeOffset]::UtcNow;status='timeout-unverified';exit_code=$null})
            throw "Guest step timed out: $($step.Id). No process termination was requested."
        }
        [IO.File]::WriteAllText((Join-Path $output ($step.Id + '.stdout.txt')), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $output ($step.Id + '.stderr.txt')), $stderr.GetAwaiter().GetResult())
        $records.Add(@{id=$step.Id;command=$exe;arguments=$step.Args;started_utc=$begin;finished_utc=[DateTimeOffset]::UtcNow;status='completed-not-proof-of-fix';exit_code=$process.ExitCode})
        if ($process.ExitCode -notin @(0,3010)) { throw "Unexpected servicing exit code $($process.ExitCode): $($step.Id). Review the guest evidence; do not claim a repaired machine." }
        $process.Dispose()
    }
} finally {
    @{format='baseera-native-vm-validation-v1';commit=$env:GITHUB_SHA;runner=$env:RUNNER_OS;image=$env:ImageOS;image_version=$env:ImageVersion;windows=[Environment]::OSVersion.VersionString;started_utc=$started;finished_utc=[DateTimeOffset]::UtcNow;steps=$records;notice='Disposable hosted Windows Server VM only; not Windows 10/11 coverage. Exit codes do not prove user symptoms fixed. No reboot requested.'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding utf8
}
