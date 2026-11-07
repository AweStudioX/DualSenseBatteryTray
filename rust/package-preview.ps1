param([string]$Destination = (Join-Path $PSScriptRoot 'target\native-preview'))
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$toolchain = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Directory -ErrorAction SilentlyContinue |
    Where-Object Name -Like 'MartinStorsjo.LLVM-MinGW.UCRT_*' | Select-Object -First 1
if (!$toolchain) { throw 'LLVM-MinGW toolchain was not found.' }
$toolbin = Get-ChildItem $toolchain.FullName -Directory |
    Where-Object Name -Like 'llvm-mingw-*-x86_64' | Select-Object -First 1 |
    ForEach-Object { Join-Path $_.FullName 'bin' }
if (!$toolbin) { throw 'LLVM-MinGW binaries were not found.' }
$env:PATH = "$toolbin;$env:PATH"
$env:CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER = Join-Path $toolbin 'x86_64-w64-mingw32-clang.exe'
$assetDir = Join-Path $PSScriptRoot 'target\native-preview'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
$appIcon = Join-Path $repo 'src\DualSenseBatteryTray.App\Assets\App\dualsense-disconnected.ico'
Copy-Item -LiteralPath $appIcon -Destination (Join-Path $assetDir 'dualsense-disconnected.ico') -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSES\lucide-ISC.txt') -Destination (Join-Path $assetDir 'lucide-ISC.txt') -Force
& dotnet run --project (Join-Path $repo 'tools\DualSenseBatteryTray.IconExport\DualSenseBatteryTray.IconExport.csproj') --configuration Release -- $assetDir
if ($LASTEXITCODE -ne 0) { throw 'Icon export failed.' }
$iconHash = (Get-FileHash -LiteralPath (Join-Path $assetDir 'dualsense-disconnected.ico') -Algorithm SHA256).Hash.Substring(0, 12)
$resource = Join-Path $PSScriptRoot "target\app-icon-$iconHash.o"
Push-Location $PSScriptRoot
try {
    & (Join-Path $toolbin 'x86_64-w64-mingw32-windres.exe') -i 'app-icon.rc' -o $resource -O coff
    if ($LASTEXITCODE -ne 0) { throw 'Application icon resource compilation failed.' }
    & (Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe') rustc --release --target x86_64-pc-windows-gnullvm --bin controller-battery-native -- -C "link-arg=$resource"
    if ($LASTEXITCODE -ne 0) { throw 'Rust preview build failed.' }
}
finally { Pop-Location }
$binary = Join-Path $PSScriptRoot 'target\x86_64-pc-windows-gnullvm\release\controller-battery-native.exe'
if (!(Test-Path -LiteralPath $binary)) { throw 'Build the Rust release binary before packaging.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$obsoleteBody = Join-Path $Destination 'dualsense-offline-body.ico'
if (Test-Path -LiteralPath $obsoleteBody) { Remove-Item -LiteralPath $obsoleteBody }
Copy-Item -LiteralPath $binary -Destination (Join-Path $Destination 'ControllerBatteryNativePreview.exe') -Force
if ([IO.Path]::GetFullPath($Destination) -ne [IO.Path]::GetFullPath($assetDir)) {
    Copy-Item -Path (Join-Path $assetDir '*.ico') -Destination $Destination -Force
    Copy-Item -LiteralPath (Join-Path $assetDir 'lucide-ISC.txt') -Destination $Destination -Force
}
$unwind = Get-ChildItem $toolchain.FullName -Directory |
    Where-Object Name -Like 'llvm-mingw-*-x86_64' | Select-Object -First 1 |
    ForEach-Object { Join-Path $_.FullName 'x86_64-w64-mingw32\bin\libunwind.dll' }
if (!$unwind -or !(Test-Path -LiteralPath $unwind)) { throw 'x64 LLVM-MinGW libunwind.dll was not found.' }
Copy-Item -LiteralPath $unwind -Destination (Join-Path $Destination 'libunwind.dll') -Force
Get-ChildItem -LiteralPath $Destination | Where-Object Name -Match '^(ControllerBatteryNativePreview|dualsense-disconnected|disconnected-|window-)' | Select-Object Name,Length
