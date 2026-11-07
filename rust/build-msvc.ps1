$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$asset = Join-Path $PSScriptRoot 'target\native-preview'
New-Item -ItemType Directory -Path $asset -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'src\DualSenseBatteryTray.App\Assets\App\dualsense-disconnected.ico') -Destination (Join-Path $asset 'dualsense-disconnected.ico') -Force
$rc = Get-Command 'rc.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $rc) {
    $kit = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $rc = Get-ChildItem -LiteralPath $kit -Filter 'rc.exe' -Recurse -ErrorAction SilentlyContinue |
        Where-Object FullName -Match '\\x64\\' | Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $rc) { throw 'Windows x64 resource compiler rc.exe was not found.' }
$resource = Join-Path $asset 'app-icon.res'
Push-Location $PSScriptRoot
try {
    & $rc '/nologo' "/fo$resource" 'app-icon.rc'
    if ($LASTEXITCODE -ne 0) { throw 'Application icon resource compilation failed.' }
    cargo rustc --locked --release --target x86_64-pc-windows-msvc --bin controller-battery-native -- -C "link-arg=$resource"
    if ($LASTEXITCODE -ne 0) { throw 'MSVC native build failed.' }
}
finally { Pop-Location }
