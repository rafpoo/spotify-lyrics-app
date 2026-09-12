param([int]$DurationSeconds = 180)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable = Join-Path $repository 'artifacts\release\portable\LyricFloat.exe'
if (Get-Process LyricFloat -ErrorAction SilentlyContinue) { throw 'Exit existing LyricFloat copies before running this release check.' }
$primary = Start-Process -FilePath $executable -PassThru
try {
    if (-not $primary.WaitForInputIdle(15000)) { throw 'Published application did not become idle.' }
    $second = Start-Process -FilePath $executable -PassThru
    if (-not $second.WaitForExit(10000) -or $second.ExitCode -ne 0) { throw 'Second launch did not exit cleanly.' }
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        $primary.Refresh()
        if ($primary.MainWindowTitle -eq 'LyricFloat Settings') { break }
        Start-Sleep -Milliseconds 250
    }
    if ($primary.HasExited -or $primary.MainWindowTitle -ne 'LyricFloat Settings') { throw 'Existing instance did not open Settings.' }
    $primary.Refresh()
    $initialCpu = $primary.TotalProcessorTime.TotalMilliseconds
    $initialMemory = $primary.WorkingSet64
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $DurationSeconds) {
        Start-Sleep -Seconds ([Math]::Min(30, [Math]::Max(1, $DurationSeconds - [int]$watch.Elapsed.TotalSeconds)))
        $primary.Refresh()
        if ($primary.HasExited) { throw 'Published application exited during observation.' }
        Write-Output ('Observed {0}s; working set {1:N1} MB' -f [int]$watch.Elapsed.TotalSeconds, ($primary.WorkingSet64/1MB))
    }
    $report = [ordered]@{
        Version = $primary.MainModule.FileVersionInfo.ProductVersion
        DurationSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 1)
        AverageCpuPercentOfOneCore = [Math]::Round(($primary.TotalProcessorTime.TotalMilliseconds-$initialCpu)/$watch.Elapsed.TotalMilliseconds*100, 3)
        InitialWorkingSetMB = [Math]::Round($initialMemory/1MB, 2)
        FinalWorkingSetMB = [Math]::Round($primary.WorkingSet64/1MB, 2)
        SingleInstanceActivation = $true
        Observation = 'Desktop observation including Settings; UI interactions may affect measurements. Live Spotify playback requires separate authorization.'
    }
    $exitRequest = Start-Process -FilePath $executable -ArgumentList '--exit' -PassThru
    if (-not $exitRequest.WaitForExit(10000) -or -not $primary.WaitForExit(15000)) { throw 'Graceful shutdown did not finish.' }
    $report.CleanExit = ($primary.ExitCode -eq 0)
    $report.OAuthListenerRemaining = [bool]([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 43821)
    $path = Join-Path $repository 'artifacts\release\runtime-check.json'
    $report | ConvertTo-Json | Set-Content -LiteralPath $path
    $report | ConvertTo-Json
} finally {
    $primary.Refresh()
    if (-not $primary.HasExited) {
        Start-Process -FilePath $executable -ArgumentList '--exit' -Wait
    }
}
