[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SafeUninstallDestination {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProgramsDirectory,

        [Parameter(Mandatory = $true)]
        [string] $InstallDirectory
    )

    foreach ($path in @($ProgramsDirectory, $InstallDirectory)) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if (-not $item.PSIsContainer) {
            throw "Expected an ordinary directory but found '$path'."
        }
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a reparse-point uninstall destination: '$path'."
        }
    }
}

function Test-PathComponentsAreOrdinary {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BasePath,

        [Parameter(Mandatory = $true)]
        [string] $CandidatePath
    )

    $resolvedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedCandidate = [System.IO.Path]::GetFullPath($CandidatePath)
    $basePrefix = $resolvedBase + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith(
            $basePrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $relativePath = $resolvedCandidate.Substring($basePrefix.Length)
    $currentPath = $resolvedBase
    foreach ($component in $relativePath.Split(
            [char[]] @(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar),
            [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $currentPath = Join-Path $currentPath $component
        if (-not (Test-Path -LiteralPath $currentPath)) {
            return $false
        }

        $attributes = [System.IO.File]::GetAttributes($currentPath)
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $false
        }
    }

    return $true
}

function Invoke-FileReleaseRetry {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Operation
    )

    $removeAttemptLimit = 20
    for ($attempt = 1; $attempt -le $removeAttemptLimit; $attempt++) {
        try {
            & $Operation
            return
        }
        catch [System.UnauthorizedAccessException] {
            if ($attempt -eq $removeAttemptLimit) {
                throw
            }
            Start-Sleep -Milliseconds 100
        }
        catch [System.IO.IOException] {
            if ($attempt -eq $removeAttemptLimit) {
                throw
            }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Remove-FileSystemEntrySafely {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [System.IO.FileAttributes] $Attributes
    )

    $isDirectory = ($Attributes -band [System.IO.FileAttributes]::Directory) -ne 0
    $isReparsePoint = ($Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    Invoke-FileReleaseRetry -Operation {
        if (-not $isReparsePoint -and
            ($Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
            [System.IO.File]::SetAttributes(
                $Path,
                $Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly))
        }

        if ($isDirectory) {
            [System.IO.Directory]::Delete($Path, $false)
        }
        else {
            [System.IO.File]::Delete($Path)
        }
    }
}

function Remove-InstallDirectorySafely {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath
    )

    $rootAttributes = [System.IO.File]::GetAttributes($RootPath)
    if (($rootAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to recursively remove reparse-point root '$RootPath'."
    }
    if (($rootAttributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
        throw "Expected an install directory but found '$RootPath'."
    }

    foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($RootPath)) {
        $attributes = [System.IO.File]::GetAttributes($entry)
        $isDirectory = ($attributes -band [System.IO.FileAttributes]::Directory) -ne 0
        $isReparsePoint = ($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        if ($isDirectory -and -not $isReparsePoint) {
            Remove-InstallDirectorySafely -RootPath $entry
        }
        else {
            Remove-FileSystemEntrySafely -Path $entry -Attributes $attributes
        }
    }

    $finalAttributes = [System.IO.File]::GetAttributes($RootPath)
    Remove-FileSystemEntrySafely -Path $RootPath -Attributes $finalAttributes
}

$taskName = 'DualSenseBatteryTray-DeviceWatcher'
$taskPath = '\'
$programsDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Programs'))
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray'
$expectedInstallDirectory = [System.IO.Path]::GetFullPath($installDirectory)
$programsPrefix = $programsDirectory.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$installPrefix = $expectedInstallDirectory.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $expectedInstallDirectory.StartsWith(
        $programsPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to uninstall outside '$programsDirectory': '$expectedInstallDirectory'."
}
Assert-SafeUninstallDestination -ProgramsDirectory $programsDirectory `
    -InstallDirectory $expectedInstallDirectory

$task = Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath -ErrorAction SilentlyContinue
if ($null -ne $task) {
    Unregister-ScheduledTask `
        -TaskName $taskName `
        -TaskPath $taskPath `
        -Confirm:$false `
        -ErrorAction Stop
}

$processes = Get-Process -Name 'DualSenseBatteryTray.Watcher', 'DualSenseBatteryTray.App' -ErrorAction SilentlyContinue
foreach ($process in $processes) {
    try {
        $processPath = $process.Path
        if ([string]::IsNullOrWhiteSpace($processPath)) {
            Write-Warning "Could not inspect process $($process.Id); it will not be stopped."
            continue
        }

        $resolvedProcessPath = [System.IO.Path]::GetFullPath($processPath)
        if (-not $resolvedProcessPath.StartsWith(
                $installPrefix,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if (-not (Test-PathComponentsAreOrdinary `
                -BasePath $expectedInstallDirectory `
                -CandidatePath $resolvedProcessPath)) {
            Write-Warning "Process $($process.Id) uses a reparse-point or missing path; it will not be stopped."
            continue
        }

        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "Could not safely stop process $($process.Id): $($_.Exception.Message)"
    }
}

if (Test-Path -LiteralPath $expectedInstallDirectory) {
    $resolvedInstallDirectory = [System.IO.Path]::GetFullPath(
        (Resolve-Path -LiteralPath $expectedInstallDirectory -ErrorAction Stop).Path)
    if (-not $resolvedInstallDirectory.StartsWith(
            $programsPrefix,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedInstallDirectory.Equals(
            $expectedInstallDirectory,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected install path '$resolvedInstallDirectory'."
    }
    Assert-SafeUninstallDestination -ProgramsDirectory $programsDirectory `
        -InstallDirectory $resolvedInstallDirectory
    Remove-InstallDirectorySafely -RootPath $resolvedInstallDirectory
}

Write-Host "Uninstalled DualSense Battery Tray and task '$taskName'."
