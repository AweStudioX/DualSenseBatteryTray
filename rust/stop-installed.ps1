param([Parameter(Mandatory)][string]$InstallDirectory)
$ErrorActionPreference = 'Stop'
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ControllerBatteryNative'))
$actual = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $actual.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to stop processes outside $expected"
}
$exe = Join-Path $expected 'ControllerBatteryNative.exe'
$processes = @(Get-CimInstance Win32_Process -Filter "Name = 'ControllerBatteryNative.exe'" | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.Equals($exe, [StringComparison]::OrdinalIgnoreCase)
})
foreach ($process in $processes) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
}
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $remaining = @(Get-CimInstance Win32_Process -Filter "Name = 'ControllerBatteryNative.exe'" | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.Equals($exe, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($remaining.Count -eq 0) { return }
    Start-Sleep -Milliseconds 100
}
throw 'Installed native processes did not stop.'
