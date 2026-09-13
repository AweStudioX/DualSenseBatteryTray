BeforeAll {
    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $buildScript = Join-Path $projectRoot 'scripts\build-installer.ps1'
    $makensisPath = Join-Path $env:TEMP 'dualsense-nsis-3.12\nsis-3.12\makensis.exe'
}

Describe 'Windows Setup build' {
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
