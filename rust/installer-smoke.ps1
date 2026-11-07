param([Parameter(Mandatory)][string]$SetupPath)
$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$install = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ControllerBatteryNative'))
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ControllerBatteryNative'))
if ($install -ne $expected -or (Test-Path -LiteralPath $install)) {
    throw 'Smoke test requires an empty, exact native install destination.'
}
& $setup '/S'
if ($LASTEXITCODE -ne 0) { throw "Silent install failed: $LASTEXITCODE" }
$exe = Join-Path $install 'ControllerBatteryNative.exe'
for ($attempt = 0; $attempt -lt 50 -and -not (Test-Path -LiteralPath $exe -PathType Leaf); $attempt++) {
    Start-Sleep -Milliseconds 200
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Native executable was not installed.' }
$run = $null
for ($attempt = 0; $attempt -lt 50 -and -not $run; $attempt++) {
    $run = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ControllerBatteryNative' -ErrorAction SilentlyContinue).ControllerBatteryNative
    if (-not $run) { Start-Sleep -Milliseconds 200 }
}
if ($run -ne ('"' + $exe + '" --watcher')) { throw 'Sign-in watcher registration is incorrect.' }
& $setup '/S'
if ($LASTEXITCODE -ne 0) { throw "Silent upgrade failed: $LASTEXITCODE" }
Start-Sleep -Seconds 3
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Native executable disappeared during upgrade.' }
$watchers = @(Get-CimInstance Win32_Process -Filter "Name = 'ControllerBatteryNative.exe'" | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.Equals($exe, [StringComparison]::OrdinalIgnoreCase) -and
    $_.CommandLine -match '--watcher'
})
if ($watchers.Count -ne 1) { throw "Expected one watcher after upgrade, found $($watchers.Count)." }
$uninstaller = Join-Path $install 'Uninstall.exe'
& $uninstaller '/S'
if ($LASTEXITCODE -ne 0) { throw "Silent uninstall failed: $LASTEXITCODE" }
for ($attempt = 0; $attempt -lt 50 -and (Test-Path -LiteralPath $exe -PathType Leaf); $attempt++) {
    Start-Sleep -Milliseconds 200
}
if (Test-Path -LiteralPath $exe) { throw 'Native executable remained after uninstall.' }
if (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ControllerBatteryNative' -ErrorAction SilentlyContinue) {
    throw 'Sign-in watcher registration remained after uninstall.'
}
Write-Host 'Native install, sign-in registration, and uninstall smoke test passed.'
