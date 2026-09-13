[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SetupPath,
    [Parameter(Mandatory)] [string] $LegacyPublishDirectory
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installer smoke test is restricted to a GitHub-hosted runner.'
}

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DualSenseBatteryTray'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DualSense Battery Tray'
$taskName = 'DualSenseBatteryTray-DeviceWatcher'
$legacyScript = Join-Path $projectRoot 'scripts\install.ps1'
$legacyUninstall = Join-Path $projectRoot 'scripts\uninstall.ps1'
$sentinelCreated = $false

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

function Invoke-Setup([string] $path) {
    $process = Start-Process -FilePath $path -ArgumentList '/S' -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Setup failed with exit code $($process.ExitCode)." }
}

function Invoke-Uninstaller {
    $path = Join-Path $installRoot 'Uninstall.exe'
    $process = Start-Process -FilePath $path -ArgumentList '/S' -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Uninstaller failed with exit code $($process.ExitCode)." }
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ((Test-Path -LiteralPath $installRoot) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
}

function Assert-Installed {
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'DualSenseBatteryTray.App.exe') -PathType Leaf) 'Installed App is missing.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'DualSenseBatteryTray.Watcher.exe') -PathType Leaf) 'Installed Watcher is missing.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'Uninstall.exe') -PathType Leaf) 'Installed uninstaller is missing.'
    Assert-True (Test-Path -LiteralPath $uninstallKey) 'Installed apps registration is missing.'
    Assert-True (Test-Path -LiteralPath $startMenu -PathType Container) 'Start Menu shortcuts are missing.'
    Assert-True ($null -ne (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) 'Watcher task is missing.'
}

function Assert-Uninstalled {
    Assert-True (-not (Test-Path -LiteralPath $installRoot)) 'Installation directory remains.'
    Assert-True (-not (Test-Path -LiteralPath $uninstallKey)) 'Installed apps registration remains.'
    Assert-True (-not (Test-Path -LiteralPath $startMenu)) 'Start Menu shortcuts remain.'
    Assert-True ($null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) 'Watcher task remains.'
}

if (-not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) { throw "Setup not found: '$SetupPath'." }
if (-not (Test-Path -LiteralPath $LegacyPublishDirectory -PathType Container)) { throw "Legacy publish directory not found: '$LegacyPublishDirectory'." }
Assert-Uninstalled

try {
    Write-Host 'Fresh Setup install'
    Invoke-Setup $SetupPath
    Assert-Installed

    Write-Host 'Setup-to-Setup upgrade'
    Invoke-Setup $SetupPath
    Assert-Installed
    Invoke-Uninstaller
    Assert-Uninstalled

    Write-Host 'Legacy script-to-Setup upgrade'
    & $legacyScript -PublishDirectory $LegacyPublishDirectory
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'DualSenseBatteryTray.App.exe')) 'Legacy install failed.'
    Invoke-Setup $SetupPath
    Assert-Installed
    Invoke-Uninstaller
    Assert-Uninstalled

    Write-Host 'Blocked destination failure'
    [System.IO.File]::WriteAllText($installRoot, 'setup-failure-sentinel')
    $sentinelCreated = $true
    $failedSetup = Start-Process -FilePath $SetupPath -ArgumentList '/S' -PassThru -Wait -WindowStyle Hidden
    Assert-True ($failedSetup.ExitCode -ne 0) 'Setup reported success for a blocked destination.'
    Assert-True ((Get-Content -LiteralPath $installRoot -Raw) -eq 'setup-failure-sentinel') 'Setup changed the sentinel file.'
    Assert-True (-not (Test-Path -LiteralPath $uninstallKey)) 'Failed Setup wrote an Installed apps record.'
    Assert-True ($null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) 'Failed Setup left a task.'
}
finally {
    if (Test-Path -LiteralPath $installRoot -PathType Container) {
        if (Test-Path -LiteralPath (Join-Path $installRoot 'Uninstall.exe') -PathType Leaf) {
            Invoke-Uninstaller
        }
        else {
            & $legacyUninstall
        }
    }
    if ($sentinelCreated -and (Test-Path -LiteralPath $installRoot -PathType Leaf)) {
        Assert-True ((Get-Content -LiteralPath $installRoot -Raw) -eq 'setup-failure-sentinel') 'Unexpected file at sentinel location.'
        Remove-Item -LiteralPath $installRoot -Force
    }
}

Assert-Uninstalled
Write-Host 'Installer smoke test passed.'
