BeforeAll {
    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $buildScript = Join-Path $projectRoot 'scripts\build-installer.ps1'
    $mergeScript = Join-Path $projectRoot 'scripts\merge-publish.ps1'
    $makensisPath = Join-Path $env:TEMP 'dualsense-nsis-3.12\nsis-3.12\makensis.exe'
}

Describe 'Shared self-contained runtime packaging' {
    It 'publishes both executables as multi-file self-contained applications' {
        foreach ($relativePath in @(
            'src\DualSenseBatteryTray.App\DualSenseBatteryTray.App.csproj',
            'src\DualSenseBatteryTray.Watcher\DualSenseBatteryTray.Watcher.csproj')) {
            [xml] $project = Get-Content -LiteralPath (Join-Path $projectRoot $relativePath)
            $properties = @($project.Project.PropertyGroup)

            ($properties.PublishSingleFile | Where-Object { $_ -eq 'false' }) |
                Should -Not -BeNullOrEmpty
            $properties.IncludeNativeLibrariesForSelfExtract | Should -BeNullOrEmpty
        }
    }

    It 'merges both publish outputs in release CI' {
        $workflow = Get-Content -LiteralPath (Join-Path $projectRoot '.github\workflows\release.yml') -Raw

        ([regex]::Matches($workflow, '-p:PublishSingleFile=false').Count) | Should -Be 2
        $workflow | Should -Match 'scripts/merge-publish\.ps1'
        $workflow | Should -Not -Match 'IncludeNativeLibrariesForSelfExtract'
    }

    It 'deduplicates identical runtime files and preserves unique files' {
        $app = Join-Path $TestDrive 'app'
        $watcher = Join-Path $TestDrive 'watcher'
        $output = Join-Path $TestDrive 'merged'
        New-Item -ItemType Directory -Path $app, $watcher | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $app 'DualSenseBatteryTray.App.exe'), [byte[]] @(1))
        [System.IO.File]::WriteAllBytes((Join-Path $watcher 'DualSenseBatteryTray.Watcher.exe'), [byte[]] @(2))
        [System.IO.File]::WriteAllBytes((Join-Path $app 'shared.dll'), [byte[]] @(3, 4))
        [System.IO.File]::WriteAllBytes((Join-Path $watcher 'shared.dll'), [byte[]] @(3, 4))
        [System.IO.File]::WriteAllBytes((Join-Path $app 'app-only.dll'), [byte[]] @(5))
        [System.IO.File]::WriteAllBytes((Join-Path $watcher 'watcher-only.dll'), [byte[]] @(6))

        & $mergeScript -AppDirectory $app -WatcherDirectory $watcher -OutputDirectory $output

        @(Get-ChildItem -LiteralPath $output -File).Count | Should -Be 5
        (Get-Content -LiteralPath (Join-Path $output 'shared.dll') -AsByteStream) |
            Should -Be ([byte[]] @(3, 4))
    }

    It 'rejects different files with the same relative path' {
        $app = Join-Path $TestDrive 'conflict-app'
        $watcher = Join-Path $TestDrive 'conflict-watcher'
        $output = Join-Path $TestDrive 'conflict-output'
        New-Item -ItemType Directory -Path $app, $watcher | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $app 'DualSenseBatteryTray.App.exe'), [byte[]] @(1))
        [System.IO.File]::WriteAllBytes((Join-Path $watcher 'DualSenseBatteryTray.Watcher.exe'), [byte[]] @(2))
        [System.IO.File]::WriteAllBytes((Join-Path $app 'shared.dll'), [byte[]] @(3))
        [System.IO.File]::WriteAllBytes((Join-Path $watcher 'shared.dll'), [byte[]] @(4))

        {
            & $mergeScript -AppDirectory $app -WatcherDirectory $watcher -OutputDirectory $output
        } | Should -Throw "*different files at 'shared.dll'*"
    }

    It 'recursively embeds the merged publish payload in Setup' {
        $source = Get-Content -LiteralPath (
            Join-Path $projectRoot 'installer\DualSenseBatteryTray.nsi') -Raw

        $source | Should -Match 'File /r "\$\{PUBLISH_DIR\}\\\*\.\*"'
        $source | Should -Match 'SetCompressor /SOLID lzma'
    }
}

Describe 'Windows Setup build' {
    It 'runs lifecycle scripts in 64-bit Windows PowerShell' {
        $source = Get-Content -LiteralPath (
            Join-Path $projectRoot 'installer\DualSenseBatteryTray.nsi') -Raw

        ([regex]::Matches(
            $source,
            '\$WINDIR\\Sysnative\\WindowsPowerShell\\v1\.0\\powershell\.exe').Count) |
            Should -Be 2
        $source | Should -Not -Match '\$SYSDIR\\WindowsPowerShell'
    }

    It 'rejects an invalid version before invoking the compiler' {
        {
            & $buildScript `
                -PublishDirectory $TestDrive `
                -MakensisPath $makensisPath `
                -Version '1.0.1; malicious' `
                -OutputPath (Join-Path $TestDrive 'Setup.exe')
        } | Should -Throw '*Invalid version*'
    }

    It 'rejects a publish directory without both executables' {
        $appPath = Join-Path $TestDrive 'DualSenseBatteryTray.App.exe'
        [System.IO.File]::WriteAllBytes($appPath, [byte[]] @(1))

        {
            & $buildScript `
                -PublishDirectory $TestDrive `
                -MakensisPath $makensisPath `
                -Version '1.0.1' `
                -OutputPath (Join-Path $TestDrive 'Setup.exe')
        } | Should -Throw '*DualSenseBatteryTray.Watcher.exe*'
    }

    It 'compiles one nonempty Setup.exe from valid inputs' {
        if (-not (Test-Path -LiteralPath $makensisPath -PathType Leaf)) {
            Set-ItResult -Skipped -Because 'The pinned NSIS compiler is unavailable.'
            return
        }

        $publish = Join-Path $TestDrive 'publish'
        New-Item -ItemType Directory -Path $publish | Out-Null
        foreach ($name in 'DualSenseBatteryTray.App.exe', 'DualSenseBatteryTray.Watcher.exe') {
            [System.IO.File]::WriteAllBytes((Join-Path $publish $name), [byte[]] @(1, 2, 3))
        }
        $output = Join-Path $TestDrive 'Setup.exe'
        & $buildScript -PublishDirectory $publish -MakensisPath $makensisPath -Version '1.0.1' -OutputPath $output

        $output | Should -Exist
        (Get-Item -LiteralPath $output).Length | Should -BeGreaterThan 0
    }
}
