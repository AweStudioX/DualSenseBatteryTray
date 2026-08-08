BeforeAll {
    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $installScriptPath = Join-Path $projectRoot 'scripts\install.ps1'
    $uninstallScriptPath = Join-Path $projectRoot 'scripts\uninstall.ps1'
    $taskXmlPath = Join-Path $projectRoot 'scripts\device-watcher-task.xml'

    if ($null -eq ('ReleasingFileLock' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;

public sealed class ReleasingFileLock : IDisposable
{
    private FileStream stream;
    private readonly Timer timer;

    public ReleasingFileLock(string path, int releaseAfterMilliseconds)
        : this(path, releaseAfterMilliseconds, false)
    {
    }

    public ReleasingFileLock(string path, int releaseAfterMilliseconds, bool exclusive)
    {
        FileShare share = exclusive ? FileShare.None : FileShare.Read;
        stream = File.Open(path, FileMode.Open, FileAccess.Read, share);
        timer = new Timer(_ => Release(), null, releaseAfterMilliseconds, Timeout.Infinite);
    }

    private void Release()
    {
        FileStream previous = Interlocked.Exchange(ref stream, null);
        if (previous != null)
        {
            previous.Dispose();
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        Release();
    }
}
'@
    }

    function Get-ScriptFunctionDefinitions([string] $path) {
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $path,
            [ref] $tokens,
            [ref] $parseErrors)
        if ($parseErrors.Count -ne 0) {
            throw "Could not parse '$path': $($parseErrors[0].Message)"
        }

        @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
                }, $true))
    }

    function Invoke-ScriptFunction(
        [string] $path,
        [string] $name,
        [object[]] $arguments) {
        $definitions = @(Get-ScriptFunctionDefinitions $path)
        if ($name -notin $definitions.Name) {
            throw "Function '$name' was not found in '$path'."
        }

        $harnessText = ($definitions.Extent.Text -join [Environment]::NewLine)
        $harnessText += [Environment]::NewLine + "$name @args"
        & ([scriptblock]::Create($harnessText)) @arguments
    }
}

Describe 'Device watcher installation artifacts' {
    It 'provides the install, uninstall, and scheduled-task files' {
        $installScriptPath | Should -Exist
        $uninstallScriptPath | Should -Exist
        $taskXmlPath | Should -Exist
    }
}

Describe 'Device watcher scheduled task XML' {
    BeforeAll {
        [xml] $taskXml = if (Test-Path -LiteralPath $taskXmlPath) {
            Get-Content -LiteralPath $taskXmlPath -Raw
        }
        else {
            '<Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task" />'
        }
        $namespace = [System.Xml.XmlNamespaceManager]::new($taskXml.NameTable)
        $namespace.AddNamespace('task', $taskXml.DocumentElement.NamespaceURI)
    }

    It 'does not declare a byte encoding for the in-memory registration string' {
        $taskXml.FirstChild.Encoding | Should -BeNullOrEmpty
    }

    It 'uses an enabled logon trigger scoped to the current user token' {
        $trigger = $taskXml.SelectSingleNode(
            '/task:Task/task:Triggers/task:LogonTrigger',
            $namespace)

        $trigger | Should -Not -BeNullOrEmpty
        $trigger.Enabled | Should -Be 'true'
        $trigger.UserId | Should -Be '${CURRENT_USER_SID}'
    }

    It 'runs as the current interactive user with least privilege' {
        $principal = $taskXml.SelectSingleNode(
            '/task:Task/task:Principals/task:Principal',
            $namespace)

        $principal.UserId | Should -Be '${CURRENT_USER_SID}'
        $principal.LogonType | Should -Be 'InteractiveToken'
        $principal.RunLevel | Should -Be 'LeastPrivilege'
    }

    It 'runs the watcher executable hidden' {
        $command = $taskXml.SelectSingleNode(
            '/task:Task/task:Actions/task:Exec/task:Command',
            $namespace)
        $hidden = $taskXml.SelectSingleNode(
            '/task:Task/task:Settings/task:Hidden',
            $namespace)

        $command.InnerText | Should -Be '${WATCHER_PATH}'
        $hidden.InnerText | Should -Be 'true'
    }
}

Describe 'Install script contract' {
    BeforeAll {
        $installText = if (Test-Path -LiteralPath $installScriptPath) {
            Get-Content -LiteralPath $installScriptPath -Raw
        }
        else {
            ''
        }
    }

    It 'requires a publish directory and uses the fixed current-user destination' {
        $installText | Should -Match '(?s)\[Parameter\(Mandatory[^)]*\)\].*\$PublishDirectory'
        $installText | Should -Match 'Join-Path\s+\$env:LOCALAPPDATA\s+[''"]Programs\\DualSenseBatteryTray[''"]'
    }

    It 'uses only the exact scheduled task name' {
        [regex]::Matches($installText, 'DualSenseBatteryTray-[A-Za-z0-9_-]+').Value |
            Select-Object -Unique |
            Should -Be @('DualSenseBatteryTray-DeviceWatcher')
    }

    It 'resolves and boundary-checks the destination before copying files' {
        $resolveIndex = $installText.IndexOf('$resolvedInstallDirectory =')
        $boundaryIndex = $installText.IndexOf('$resolvedInstallDirectory.StartsWith(')
        $reparseIndex = $installText.LastIndexOf(
            'Assert-SafeInstallDestination -ProgramsDirectory')
        $copyIndex = $installText.IndexOf('Copy-Item')

        $resolveIndex | Should -BeGreaterThan -1
        $boundaryIndex | Should -BeGreaterThan $resolveIndex
        $reparseIndex | Should -BeGreaterThan $boundaryIndex
        $copyIndex | Should -BeGreaterThan $reparseIndex
    }

    It 'substitutes watcher and current-user tokens before registering and starting the task' {
        $installText | Should -Match '\.Replace\([''"]\$\{WATCHER_PATH\}[''"]'
        $installText | Should -Match '\.Replace\([''"]\$\{CURRENT_USER_SID\}[''"]'
        $installText | Should -Match "Join-Path\s+\`$resolvedInstallDirectory\s+'DualSenseBatteryTray\.Watcher\.exe'"
        $installText | Should -Match 'Register-ScheduledTask'
        $installText | Should -Match 'Start-ScheduledTask'
    }

    It 'preserves copied files and prints the exact manual watcher path when registration fails' {
        $installText | Should -Match '(?s)catch\s*\{.*Start manually:\s*\$watcherPath.*throw'
        $installText | Should -Not -Match '(?s)catch\s*\{.*Remove-Item'
    }
}

Describe 'Uninstall script contract' {
    BeforeAll {
        $uninstallText = if (Test-Path -LiteralPath $uninstallScriptPath) {
            Get-Content -LiteralPath $uninstallScriptPath -Raw
        }
        else {
            ''
        }
    }

    It 'uses only the exact scheduled task name and fixed current-user destination' {
        [regex]::Matches($uninstallText, 'DualSenseBatteryTray-[A-Za-z0-9_-]+').Value |
            Select-Object -Unique |
            Should -Be @('DualSenseBatteryTray-DeviceWatcher')
        $uninstallText | Should -Match 'Join-Path\s+\$env:LOCALAPPDATA\s+[''"]Programs\\DualSenseBatteryTray[''"]'
    }

    It 'only selects the watcher and tray process names for shutdown' {
        $uninstallText | Should -Match 'Get-Process\s+-Name\s+[''"]DualSenseBatteryTray\.Watcher[''"],\s*[''"]DualSenseBatteryTray\.App[''"]'
    }

    It 'checks each executable path is inside the resolved install directory before stopping it' {
        $pathIndex = $uninstallText.IndexOf('$resolvedProcessPath =')
        $boundaryIndex = $uninstallText.IndexOf('$resolvedProcessPath.StartsWith(')
        $stopIndex = $uninstallText.IndexOf('Stop-Process')

        $pathIndex | Should -BeGreaterThan -1
        $boundaryIndex | Should -BeGreaterThan $pathIndex
        $stopIndex | Should -BeGreaterThan $boundaryIndex
    }

    It 'resolves and boundary-checks the existing destination before recursive removal' {
        $resolveIndex = $uninstallText.IndexOf('$resolvedInstallDirectory =')
        $boundaryIndex = $uninstallText.LastIndexOf('$resolvedInstallDirectory.StartsWith(')
        $removeIndex = $uninstallText.LastIndexOf(
            'Remove-InstallDirectorySafely -RootPath $resolvedInstallDirectory')

        $resolveIndex | Should -BeGreaterThan -1
        $boundaryIndex | Should -BeGreaterThan $resolveIndex
        $removeIndex | Should -BeGreaterThan $boundaryIndex
        $uninstallText | Should -Not -Match 'Remove-Item(?s:.*?)\s-Recurse'
    }

    It 'uses a bounded retry for transient executable release delays' {
        $uninstallText | Should -Match '\$removeAttemptLimit\s*=\s*\d+'
        $uninstallText | Should -Match '(?s)for\s*\(\$attempt.*catch\s*\[System\.UnauthorizedAccessException\].*Start-Sleep'
    }

    It 'avoids path APIs unavailable in Windows PowerShell 5.1' {
        $uninstallText | Should -Not -Match 'Path\]::GetRelativePath'
    }
}

Describe 'Reparse-point confinement' {
    It 'provides the install and uninstall safety functions' {
        $installFunctions = (Get-ScriptFunctionDefinitions $installScriptPath).Name
        $uninstallFunctions = (Get-ScriptFunctionDefinitions $uninstallScriptPath).Name

        $installFunctions | Should -Contain 'Assert-SafeInstallDestination'
        $uninstallFunctions | Should -Contain 'Assert-SafeUninstallDestination'
        $uninstallFunctions | Should -Contain 'Test-PathComponentsAreOrdinary'
        $uninstallFunctions | Should -Contain 'Remove-InstallDirectorySafely'
    }

    It 'rejects a reparse-point Programs directory before installation' {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $localAppData = Join-Path $caseRoot 'LocalAppData'
        $outside = Join-Path $caseRoot 'OutsidePrograms'
        $programs = Join-Path $localAppData 'Programs'
        $install = Join-Path $programs 'DualSenseBatteryTray'
        $null = New-Item -ItemType Directory -Path $localAppData, $outside -Force
        $null = New-Item -ItemType Junction -Path $programs -Target $outside

        try {
            {
                Invoke-ScriptFunction $installScriptPath `
                    'Assert-SafeInstallDestination' @($programs, $install)
            } | Should -Throw '*reparse*'
        }
        finally {
            [System.IO.Directory]::Delete($programs, $false)
        }
    }

    It 'rejects a nested destination junction before installation copies files' {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $programs = Join-Path $caseRoot 'Programs'
        $install = Join-Path $programs 'DualSenseBatteryTray'
        $outside = Join-Path $caseRoot 'Outside'
        $nestedLink = Join-Path $install 'LinkedDirectory'
        $null = New-Item -ItemType Directory -Path $install, $outside -Force
        $null = New-Item -ItemType Junction -Path $nestedLink -Target $outside

        try {
            {
                Invoke-ScriptFunction $installScriptPath `
                    'Assert-SafeInstallDestination' @($programs, $install)
            } | Should -Throw '*reparse*'
        }
        finally {
            [System.IO.Directory]::Delete($nestedLink, $false)
        }
    }

    It 'rejects a reparse-point install root before uninstall mutations' {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $programs = Join-Path $caseRoot 'Programs'
        $outside = Join-Path $caseRoot 'OutsideInstall'
        $install = Join-Path $programs 'DualSenseBatteryTray'
        $null = New-Item -ItemType Directory -Path $programs, $outside -Force
        $null = New-Item -ItemType Junction -Path $install -Target $outside

        try {
            {
                Invoke-ScriptFunction $uninstallScriptPath `
                    'Assert-SafeUninstallDestination' @($programs, $install)
            } | Should -Throw '*reparse*'
        }
        finally {
            [System.IO.Directory]::Delete($install, $false)
        }
    }

    It 'does not consider an executable reached through a junction ordinary' {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $install = Join-Path $caseRoot 'DualSenseBatteryTray'
        $outside = Join-Path $caseRoot 'OutsideProcess'
        $link = Join-Path $install 'LinkedDirectory'
        $executable = Join-Path $link 'DualSenseBatteryTray.App.exe'
        $null = New-Item -ItemType Directory -Path $install, $outside -Force
        $null = New-Item -ItemType File -Path (Join-Path $outside 'DualSenseBatteryTray.App.exe')
        $null = New-Item -ItemType Junction -Path $link -Target $outside

        try {
            $result = Invoke-ScriptFunction $uninstallScriptPath `
                'Test-PathComponentsAreOrdinary' @($install, $executable)

            $result | Should -BeFalse
        }
        finally {
            [System.IO.Directory]::Delete($link, $false)
        }
    }

    It 'removes nested junctions without traversing or deleting their targets' {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $install = Join-Path $caseRoot 'DualSenseBatteryTray'
        $ordinaryDirectory = Join-Path $install 'Ordinary'
        $outside = Join-Path $caseRoot 'OutsideSentinel'
        $link = Join-Path $ordinaryDirectory 'LinkedDirectory'
        $sentinel = Join-Path $outside 'keep.txt'
        $null = New-Item -ItemType Directory -Path $ordinaryDirectory, $outside -Force
        Set-Content -LiteralPath (Join-Path $ordinaryDirectory 'delete.txt') -Value 'delete'
        Set-Content -LiteralPath $sentinel -Value 'keep'
        $null = New-Item -ItemType Junction -Path $link -Target $outside

        Invoke-ScriptFunction $uninstallScriptPath `
            'Remove-InstallDirectorySafely' @($install)

        $install | Should -Not -Exist
        $outside | Should -Exist
        $sentinel | Should -Exist
    }

    It 'retries a locked executable until Windows releases it' {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $install = Join-Path $caseRoot 'DualSenseBatteryTray'
        $executable = Join-Path $install 'DualSenseBatteryTray.Watcher.exe'
        $null = New-Item -ItemType Directory -Path $install -Force
        Set-Content -LiteralPath $executable -Value 'locked executable'
        $fileLock = [ReleasingFileLock]::new($executable, 250)

        try {
            Invoke-ScriptFunction $uninstallScriptPath `
                'Remove-InstallDirectorySafely' @($install)
        }
        finally {
            $fileLock.Dispose()
        }

        $install | Should -Not -Exist
    }
}

Describe 'Root scheduled-task isolation' {
    BeforeEach {
        $script:originalLocalAppData = $env:LOCALAPPDATA
        $script:caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $env:LOCALAPPDATA = Join-Path $script:caseRoot 'LocalAppData'
        $script:publishDirectory = Join-Path $script:caseRoot 'Publish'
        $null = New-Item -ItemType Directory -Path $env:LOCALAPPDATA, $script:publishDirectory -Force
        Set-Content -LiteralPath (Join-Path $script:publishDirectory 'DualSenseBatteryTray.App.exe') -Value 'app'
        Set-Content -LiteralPath (Join-Path $script:publishDirectory 'DualSenseBatteryTray.Watcher.exe') -Value 'watcher'

        Mock Get-ScheduledTask {
            $rootTask = [pscustomobject]@{
                TaskName = 'DualSenseBatteryTray-DeviceWatcher'
                TaskPath = '\'
                State = 'Running'
            }
            $nestedTask = [pscustomobject]@{
                TaskName = 'DualSenseBatteryTray-DeviceWatcher'
                TaskPath = '\Other\'
                State = 'Running'
            }
            if ($TaskPath -eq '\') {
                return $rootTask
            }
            return @($rootTask, $nestedTask)
        }
        Mock Export-ScheduledTask { '<Task>prior root task</Task>' }
        Mock Stop-ScheduledTask {}
        Mock Register-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Unregister-ScheduledTask {}
        Mock Get-Process { @() }
    }

    AfterEach {
        $env:LOCALAPPDATA = $script:originalLocalAppData
    }

    It 'registers and starts only the exact root task' {
        & $installScriptPath -PublishDirectory $script:publishDirectory

        Should -Invoke Register-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
        Should -Invoke Start-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
        Should -Not -Invoke Register-ScheduledTask -ParameterFilter { $TaskPath -eq '\Other\' }
        Should -Not -Invoke Start-ScheduledTask -ParameterFilter { $TaskPath -eq '\Other\' }
    }

    It 'unregisters only the exact root task and leaves a same-named nested task untouched' {
        $installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray'
        $null = New-Item -ItemType Directory -Path $installDirectory -Force
        Set-Content -LiteralPath (Join-Path $installDirectory 'old.txt') -Value 'old'

        & $uninstallScriptPath

        Should -Invoke Get-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
        Should -Invoke Unregister-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
        Should -Not -Invoke Unregister-ScheduledTask -ParameterFilter { $TaskPath -eq '\Other\' }
    }
}

Describe 'Transactional repeat installation' {
    BeforeEach {
        $script:originalLocalAppData = $env:LOCALAPPDATA
        $script:caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $env:LOCALAPPDATA = Join-Path $script:caseRoot 'LocalAppData'
        $script:programsDirectory = Join-Path $env:LOCALAPPDATA 'Programs'
        $script:installDirectory = Join-Path $script:programsDirectory 'DualSenseBatteryTray'
        $script:publishDirectory = Join-Path $script:caseRoot 'Publish'
        $null = New-Item -ItemType Directory `
            -Path $script:installDirectory, $script:publishDirectory `
            -Force

        Set-Content -LiteralPath (Join-Path $script:installDirectory 'DualSenseBatteryTray.App.exe') -Value 'old app'
        Set-Content -LiteralPath (Join-Path $script:installDirectory 'DualSenseBatteryTray.Watcher.exe') -Value 'old watcher'
        Set-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Value 'old version'
        Set-Content -LiteralPath (Join-Path $script:installDirectory 'old-only.txt') -Value 'old only'

        Set-Content -LiteralPath (Join-Path $script:publishDirectory 'DualSenseBatteryTray.App.exe') -Value 'new app'
        Set-Content -LiteralPath (Join-Path $script:publishDirectory 'DualSenseBatteryTray.Watcher.exe') -Value 'new watcher'
        Set-Content -LiteralPath (Join-Path $script:publishDirectory 'version.txt') -Value 'new version'
        Set-Content -LiteralPath (Join-Path $script:publishDirectory 'new-only.txt') -Value 'new only'

        Mock Get-ScheduledTask {
            if ($TaskPath -eq '\') {
                return [pscustomobject]@{
                    TaskName = 'DualSenseBatteryTray-DeviceWatcher'
                    TaskPath = '\'
                    State = 'Running'
                }
            }
            return @(
                [pscustomobject]@{
                    TaskName = 'DualSenseBatteryTray-DeviceWatcher'
                    TaskPath = '\'
                    State = 'Running'
                },
                [pscustomobject]@{
                    TaskName = 'DualSenseBatteryTray-DeviceWatcher'
                    TaskPath = '\Other\'
                    State = 'Running'
                })
        }
        Mock Export-ScheduledTask { '<Task>prior root task</Task>' }
        Mock Stop-ScheduledTask {}
        Mock Register-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Unregister-ScheduledTask {}
        Mock Get-Process {
            @(
                [pscustomobject]@{
                    Id = 4101
                    Path = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray\DualSenseBatteryTray.Watcher.exe'
                },
                [pscustomobject]@{
                    Id = 4102
                    Path = Join-Path $env:LOCALAPPDATA 'Programs\DualSenseBatteryTray\DualSenseBatteryTray.App.exe'
                })
        }
        Mock Stop-Process {}
        Mock Wait-Process {}
    }

    AfterEach {
        $env:LOCALAPPDATA = $script:originalLocalAppData
        Remove-Variable -Name DualSenseBatteryTrayTestMoveAttempts -Scope Global -ErrorAction SilentlyContinue
    }

    It 'replaces a running prior install without leaving a mixed file tree' {
        & $installScriptPath -PublishDirectory $script:publishDirectory

        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'new version'
        (Join-Path $script:installDirectory 'new-only.txt') | Should -Exist
        (Join-Path $script:installDirectory 'old-only.txt') | Should -Not -Exist
        @(Get-ChildItem -LiteralPath $script:programsDirectory -Directory -Force |
                Where-Object Name -Match '^\.DualSenseBatteryTray\.(staging|backup)-').Count |
            Should -Be 0
        Should -Invoke Stop-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
        Should -Invoke Stop-Process -Exactly 2
    }

    It 'retries a transient old-install rename failure before completing the swap' {
        $global:DualSenseBatteryTrayTestMoveAttempts = 0
        Mock Move-Item {
            param($LiteralPath, $Destination)
            if ($LiteralPath -eq $script:installDirectory) {
                $global:DualSenseBatteryTrayTestMoveAttempts++
                if ($global:DualSenseBatteryTrayTestMoveAttempts -eq 1) {
                    throw [System.IO.IOException]::new('injected transient directory lock')
                }
            }
            [System.IO.Directory]::Move($LiteralPath, $Destination)
        }

        & $installScriptPath -PublishDirectory $script:publishDirectory

        $global:DualSenseBatteryTrayTestMoveAttempts | Should -Be 2
        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'new version'
        @(Get-ChildItem -LiteralPath $script:programsDirectory -Directory -Force |
                Where-Object Name -Match '^\.DualSenseBatteryTray\.(staging|backup)-').Count |
            Should -Be 0
    }

    It 'keeps the prior install untouched when staging copy fails partway' {
        $lockedSource = Join-Path $script:publishDirectory 'DualSenseBatteryTray.Watcher.exe'
        $fileLock = [ReleasingFileLock]::new($lockedSource, 5000, $true)
        try {
            { & $installScriptPath -PublishDirectory $script:publishDirectory } |
                Should -Throw
        }
        finally {
            $fileLock.Dispose()
        }

        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'DualSenseBatteryTray.App.exe') -Raw).Trim() |
            Should -Be 'old app'
        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'old version'
        (Join-Path $script:installDirectory 'new-only.txt') | Should -Not -Exist
        @(Get-ChildItem -LiteralPath $script:programsDirectory -Directory -Force |
                Where-Object Name -Match '^\.DualSenseBatteryTray\.(staging|backup)-').Count |
            Should -Be 0
        Should -Not -Invoke Stop-ScheduledTask
        Should -Not -Invoke Stop-Process
    }

    It 'restores the prior install and task when the directory swap fails' {
        Mock Move-Item {
            param($LiteralPath, $Destination)
            if ((Split-Path -Leaf $LiteralPath) -like '.DualSenseBatteryTray.staging-*') {
                throw 'injected swap failure'
            }
            [System.IO.Directory]::Move($LiteralPath, $Destination)
        }

        { & $installScriptPath -PublishDirectory $script:publishDirectory } |
            Should -Throw '*injected swap failure*'

        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'old version'
        (Join-Path $script:installDirectory 'old-only.txt') | Should -Exist
        (Join-Path $script:installDirectory 'new-only.txt') | Should -Not -Exist
        @(Get-ChildItem -LiteralPath $script:programsDirectory -Directory -Force |
                Where-Object Name -Match '^\.DualSenseBatteryTray\.(staging|backup)-').Count |
            Should -Be 0
        Should -Invoke Start-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
    }

    It 'rolls back the prior install and root task when new task registration fails' {
        Mock Register-ScheduledTask {
            if ($Xml -ne '<Task>prior root task</Task>') {
                throw 'injected registration failure'
            }
        }

        { & $installScriptPath -PublishDirectory $script:publishDirectory } |
            Should -Throw '*injected registration failure*'

        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'old version'
        (Join-Path $script:installDirectory 'old-only.txt') | Should -Exist
        (Join-Path $script:installDirectory 'new-only.txt') | Should -Not -Exist
        @(Get-ChildItem -LiteralPath $script:programsDirectory -Directory -Force |
                Where-Object Name -Match '^\.DualSenseBatteryTray\.(staging|backup)-').Count |
            Should -Be 0
        Should -Invoke Register-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and
                $TaskPath -eq '\' -and
                $Xml -eq '<Task>prior root task</Task>'
        }
        Should -Invoke Start-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
    }

    It 'does not start a previously idle root task when registration rollback restores it' {
        Mock Get-ScheduledTask {
            if ($TaskPath -eq '\') {
                return [pscustomobject]@{
                    TaskName = 'DualSenseBatteryTray-DeviceWatcher'
                    TaskPath = '\'
                    State = 'Ready'
                }
            }
        }
        Mock Register-ScheduledTask {
            if ($Xml -ne '<Task>prior root task</Task>') {
                throw 'injected registration failure for idle task'
            }
        }

        { & $installScriptPath -PublishDirectory $script:publishDirectory } |
            Should -Throw '*injected registration failure for idle task*'

        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'old version'
        Should -Invoke Register-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and
                $TaskPath -eq '\' -and
                $Xml -eq '<Task>prior root task</Task>'
        }
        Should -Not -Invoke Start-ScheduledTask
    }

    It 'restores a prior root task even when no prior install directory exists' {
        [System.IO.Directory]::Delete($script:installDirectory, $true)
        Mock Register-ScheduledTask {
            if ($Xml -ne '<Task>prior root task</Task>') {
                throw 'injected registration failure without prior files'
            }
        }

        { & $installScriptPath -PublishDirectory $script:publishDirectory } |
            Should -Throw '*injected registration failure without prior files*'

        (Get-Content -LiteralPath (Join-Path $script:installDirectory 'version.txt') -Raw).Trim() |
            Should -Be 'new version'
        Should -Invoke Register-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and
                $TaskPath -eq '\' -and
                $Xml -eq '<Task>prior root task</Task>'
        }
        Should -Invoke Start-ScheduledTask -Exactly 1 -ParameterFilter {
            $TaskName -eq 'DualSenseBatteryTray-DeviceWatcher' -and $TaskPath -eq '\'
        }
    }
}
