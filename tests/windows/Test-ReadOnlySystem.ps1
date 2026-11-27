param([string]$ExecutablePath = 'src/Baseera.App/bin/Release/net10.0-windows/Baseera.exe', [switch]$AllowNetworkCheck)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $root
$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
$runId = [Guid]::NewGuid().ToString('N')
$output = 'artifacts/read-only-system-' + $runId
$previousData = $env:BASEERA_DATA_DIR
$previousExtract = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $env:BASEERA_DATA_DIR = Join-Path $root ('.local/read-only-system-data-' + $runId)
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $root '.local/bundle-extract'
    $arguments = @('--smoke', $output, '--read-only-system-only')
    if ($AllowNetworkCheck) { $arguments += '--allow-network-check' }
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(300000)) {
        # Only this newly created, read-only test process; no repair runs here.
        $process.Kill()
        throw 'Read-only system checks exceeded five minutes.'
    }
    $result = Join-Path $output 'read-only-system-result.txt'
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Encoding UTF8 }
    else { throw 'Read-only system checks produced no result.' }
    Write-Output ('Result: ' + (Join-Path $root $result))
    exit $process.ExitCode
}
finally {
    $env:BASEERA_DATA_DIR = $previousData
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousExtract
}
