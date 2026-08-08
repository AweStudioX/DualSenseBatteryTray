[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-NoReparsePointTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath
    )

    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($RootPath)
    while ($pendingDirectories.Count -ne 0) {
        $directory = $pendingDirectories.Pop()
        $directoryAttributes = [System.IO.File]::GetAttributes($directory)
        if (($directoryAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a reparse-point directory: '$directory'."
        }

        foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $attributes = [System.IO.File]::GetAttributes($entry)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a reparse point in the install tree: '$entry'."
            }
            if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                $pendingDirectories.Push($entry)
            }
        }
    }
}

function Assert-SafeInstallDestination {
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
            throw "Refusing a reparse-point destination: '$path'."
        }
    }

    if (Test-Path -LiteralPath $InstallDirectory) {
        Assert-NoReparsePointTree -RootPath $InstallDirectory
    }
}

function Assert-SafeProgramsSibling {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProgramsDirectory,

        [Parameter(Mandatory = $true)]
        [string] $CandidateDirectory,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedLeafPrefix
    )

    $resolvedPrograms = [System.IO.Path]::GetFullPath($ProgramsDirectory).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedCandidate = [System.IO.Path]::GetFullPath($CandidateDirectory)
    $candidateParent = [System.IO.Directory]::GetParent($resolvedCandidate)
    $candidateLeaf = [System.IO.Path]::GetFileName($resolvedCandidate)
    if ($null -eq $candidateParent -or
        -not $candidateParent.FullName.Equals(
            $resolvedPrograms,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $candidateLeaf.StartsWith(
            $ExpectedLeafPrefix,
            [System.StringComparison]::Ordinal)) {
        throw "Refusing unsafe transaction directory '$resolvedCandidate'."
    }

    Assert-SafeInstallDestination `
        -ProgramsDirectory $resolvedPrograms `
        -InstallDirectory $resolvedCandidate
}

function Assert-PublishedPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Directory
    )

    Assert-NoReparsePointTree -RootPath $Directory
    foreach ($executableName in @(
            'DualSenseBatteryTray.App.exe',
            'DualSenseBatteryTray.Watcher.exe')) {
        $executablePath = Join-Path $Directory $executableName
        if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
            throw "Published executable was not found: '$executablePath'."
        }
        if ((Get-Item -LiteralPath $executablePath -Force -ErrorAction Stop).Length -le 0) {
            throw "Published executable is empty: '$executablePath'."
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

function Stop-InstalledProcessesSafely {
    param(
        [Parameter(Mandatory = $true)]
        [string] $InstallDirectory
    )

    if (-not (Test-Path -LiteralPath $InstallDirectory -PathType Container)) {
        return
    }

    $installPrefix = $InstallDirectory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $processes = Get-Process `
        -Name 'DualSenseBatteryTray.Watcher', 'DualSenseBatteryTray.App' `
        -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        try {
            $processPath = $process.Path
        }
        catch {
            Write-Warning "Could not inspect process $($process.Id); it will not be stopped."
            continue
        }
        if ([string]::IsNullOrWhiteSpace($processPath)) {
            Write-Warning "Could not inspect process $($process.Id); it will not be stopped."
            continue
        }

        $resolvedProcessPath = [System.IO.Path]::GetFullPath($processPath)
        if (-not $resolvedProcessPath.StartsWith(
                $installPrefix,
                [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-PathComponentsAreOrdinary `
                -BasePath $InstallDirectory `
                -CandidatePath $resolvedProcessPath)) {
            continue
        }

        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }
}

$taskName = 'DualSenseBatteryTray-DeviceWatcher'
$taskPath = '\'
$taskTemplatePath = Join-Path $PSScriptRoot 'device-watcher-task.xml'
$programsDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Programs'))
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray'
$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($installDirectory)
$programsPrefix = $programsDirectory.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $resolvedInstallDirectory.StartsWith(
        $programsPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install outside '$programsDirectory': '$resolvedInstallDirectory'."
}
Assert-SafeInstallDestination -ProgramsDirectory $programsDirectory `
    -InstallDirectory $resolvedInstallDirectory

$resolvedSource = Resolve-Path -LiteralPath $PublishDirectory -ErrorAction Stop
$sourceDirectory = [System.IO.Path]::GetFullPath($resolvedSource.Path)
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    throw "Publish directory is not a directory: '$sourceDirectory'."
}
Assert-PublishedPayload -Directory $sourceDirectory

$watcherPath = Join-Path $resolvedInstallDirectory 'DualSenseBatteryTray.Watcher.exe'
$currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$escapedWatcherPath = [System.Security.SecurityElement]::Escape($watcherPath)
$escapedCurrentUserSid = [System.Security.SecurityElement]::Escape($currentUserSid)
$taskXml = Get-Content -LiteralPath $taskTemplatePath -Raw
$taskXml = $taskXml.Replace('${WATCHER_PATH}', $escapedWatcherPath)
$taskXml = $taskXml.Replace('${CURRENT_USER_SID}', $escapedCurrentUserSid)

$transactionId = [guid]::NewGuid().ToString('N')
$stagingDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $programsDirectory ".DualSenseBatteryTray.staging-$transactionId"))
$backupDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $programsDirectory ".DualSenseBatteryTray.backup-$transactionId"))
Assert-SafeProgramsSibling `
    -ProgramsDirectory $programsDirectory `
    -CandidateDirectory $stagingDirectory `
    -ExpectedLeafPrefix '.DualSenseBatteryTray.staging-'
Assert-SafeProgramsSibling `
    -ProgramsDirectory $programsDirectory `
    -CandidateDirectory $backupDirectory `
    -ExpectedLeafPrefix '.DualSenseBatteryTray.backup-'

$hadPriorInstall = Test-Path -LiteralPath $resolvedInstallDirectory -PathType Container
$priorTask = $null
$priorTaskXml = $null
$priorTaskWasRunning = $false
$priorInstallMoved = $false
$newInstallActivated = $false
$newTaskRegistrationAttempted = $false

try {
    $null = New-Item -ItemType Directory -Path $stagingDirectory -ErrorAction Stop
    Assert-SafeProgramsSibling `
        -ProgramsDirectory $programsDirectory `
        -CandidateDirectory $stagingDirectory `
        -ExpectedLeafPrefix '.DualSenseBatteryTray.staging-'
    Get-ChildItem -LiteralPath $sourceDirectory -Force |
        Copy-Item -Destination $stagingDirectory -Recurse -Force -ErrorAction Stop
    Assert-PublishedPayload -Directory $stagingDirectory

    $priorTask = Get-ScheduledTask `
        -TaskName $taskName `
        -TaskPath $taskPath `
        -ErrorAction SilentlyContinue
    if ($null -ne $priorTask) {
        $priorTaskXml = Export-ScheduledTask `
            -TaskName $taskName `
            -TaskPath $taskPath `
            -ErrorAction Stop
        $priorTaskWasRunning = [string] $priorTask.State -eq 'Running'
        Stop-ScheduledTask `
            -TaskName $taskName `
            -TaskPath $taskPath `
            -ErrorAction Stop
    }

    Stop-InstalledProcessesSafely -InstallDirectory $resolvedInstallDirectory

    if ($hadPriorInstall) {
        Assert-SafeInstallDestination `
            -ProgramsDirectory $programsDirectory `
            -InstallDirectory $resolvedInstallDirectory
        Invoke-FileReleaseRetry -Operation {
            Move-Item `
                -LiteralPath $resolvedInstallDirectory `
                -Destination $backupDirectory `
                -ErrorAction Stop
        }
        $priorInstallMoved = $true
        Assert-SafeProgramsSibling `
            -ProgramsDirectory $programsDirectory `
            -CandidateDirectory $backupDirectory `
            -ExpectedLeafPrefix '.DualSenseBatteryTray.backup-'
    }

    Invoke-FileReleaseRetry -Operation {
        Move-Item `
            -LiteralPath $stagingDirectory `
            -Destination $resolvedInstallDirectory `
            -ErrorAction Stop
    }
    $newInstallActivated = $true
    Assert-SafeInstallDestination `
        -ProgramsDirectory $programsDirectory `
        -InstallDirectory $resolvedInstallDirectory
    Assert-PublishedPayload -Directory $resolvedInstallDirectory

    $newTaskRegistrationAttempted = $true
    Register-ScheduledTask `
        -TaskName $taskName `
        -TaskPath $taskPath `
        -Xml $taskXml `
        -Force `
        -ErrorAction Stop | Out-Null
    Start-ScheduledTask -TaskName $taskName -TaskPath $taskPath -ErrorAction Stop

    if (Test-Path -LiteralPath $backupDirectory) {
        try {
            Assert-SafeProgramsSibling `
                -ProgramsDirectory $programsDirectory `
                -CandidateDirectory $backupDirectory `
                -ExpectedLeafPrefix '.DualSenseBatteryTray.backup-'
            Remove-InstallDirectorySafely -RootPath $backupDirectory
        }
        catch {
            Write-Warning "The new version is active, but backup cleanup failed at '$backupDirectory': $($_.Exception.Message)"
        }
    }
}
catch {
    $installError = $_
    $rollbackError = $null
    try {
        if ($hadPriorInstall) {
            if ($newInstallActivated -and (Test-Path -LiteralPath $resolvedInstallDirectory)) {
                Assert-SafeInstallDestination `
                    -ProgramsDirectory $programsDirectory `
                    -InstallDirectory $resolvedInstallDirectory
                Remove-InstallDirectorySafely -RootPath $resolvedInstallDirectory
                $newInstallActivated = $false
            }
            if ($priorInstallMoved -and (Test-Path -LiteralPath $backupDirectory)) {
                Assert-SafeProgramsSibling `
                    -ProgramsDirectory $programsDirectory `
                    -CandidateDirectory $backupDirectory `
                    -ExpectedLeafPrefix '.DualSenseBatteryTray.backup-'
                Invoke-FileReleaseRetry -Operation {
                    Move-Item `
                        -LiteralPath $backupDirectory `
                        -Destination $resolvedInstallDirectory `
                        -ErrorAction Stop
                }
                $priorInstallMoved = $false
            }
        }

        if ($newTaskRegistrationAttempted) {
            if ($null -ne $priorTaskXml) {
                Register-ScheduledTask `
                    -TaskName $taskName `
                    -TaskPath $taskPath `
                    -Xml $priorTaskXml `
                    -Force `
                    -ErrorAction Stop | Out-Null
            }
            else {
                $newTask = Get-ScheduledTask `
                    -TaskName $taskName `
                    -TaskPath $taskPath `
                    -ErrorAction SilentlyContinue
                if ($null -ne $newTask) {
                    Unregister-ScheduledTask `
                        -TaskName $taskName `
                        -TaskPath $taskPath `
                        -Confirm:$false `
                        -ErrorAction Stop | Out-Null
                }
            }
        }
        if ($priorTaskWasRunning) {
            Start-ScheduledTask `
                -TaskName $taskName `
                -TaskPath $taskPath `
                -ErrorAction Stop
        }
    }
    catch {
        $rollbackError = $_
    }

    Write-Warning "Installation failed: $($installError.Exception.Message)"
    Write-Host "Start manually: $watcherPath"
    if ($null -ne $rollbackError) {
        throw "Installation failed and rollback also failed. Prior backup, if present: '$backupDirectory'. Install error: $($installError.Exception.Message). Rollback error: $($rollbackError.Exception.Message)"
    }
    throw $installError
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        try {
            Assert-SafeProgramsSibling `
                -ProgramsDirectory $programsDirectory `
                -CandidateDirectory $stagingDirectory `
                -ExpectedLeafPrefix '.DualSenseBatteryTray.staging-'
            Remove-InstallDirectorySafely -RootPath $stagingDirectory
        }
        catch {
            Write-Warning "Could not clean staging directory '$stagingDirectory': $($_.Exception.Message)"
        }
    }
}

Write-Host "Installed DualSense Battery Tray to '$resolvedInstallDirectory'."
Write-Host "Scheduled task '$taskName' is registered and started."
