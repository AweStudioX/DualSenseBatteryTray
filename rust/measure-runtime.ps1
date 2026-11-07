param(
    [Parameter(Mandatory)][int]$ProcessId,
    [ValidateRange(2,120)][int]$Seconds = 15
)
$ErrorActionPreference = 'Stop'
$samples = @()
for ($second = 0; $second -lt $Seconds; $second++) {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    $samples += [pscustomobject]@{
        WorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        CpuSeconds = $process.TotalProcessorTime.TotalSeconds
        Handles = $process.HandleCount
        Threads = $process.Threads.Count
    }
    if ($second -lt $Seconds - 1) { Start-Sleep -Seconds 1 }
}
$first = $samples[0]
$last = $samples[-1]
[pscustomobject]@{
    ProcessId = $ProcessId
    Samples = $samples.Count
    WorkingSetMeanMiB = [math]::Round(($samples | Measure-Object WorkingSetMiB -Average).Average, 2)
    WorkingSetPeakMiB = [math]::Round(($samples | Measure-Object WorkingSetMiB -Maximum).Maximum, 2)
    PrivateMeanMiB = [math]::Round(($samples | Measure-Object PrivateMiB -Average).Average, 2)
    CpuSecondsPerMinute = [math]::Round((($last.CpuSeconds - $first.CpuSeconds) / ($Seconds - 1) * 60), 3)
    Handles = $last.Handles
    Threads = $last.Threads
}
