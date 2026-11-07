param(
    [Parameter(Mandatory)][string]$BinaryPath,
    [Parameter(Mandatory)][string]$Destination,
    [string]$IconDirectory = (Join-Path $PSScriptRoot 'target\native-preview')
)
$ErrorActionPreference = 'Stop'
foreach ($path in @($BinaryPath, $Destination, $IconDirectory)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw "Expected an absolute path: $path" }
}
$binary = (Resolve-Path -LiteralPath $BinaryPath).Path
$icons = (Resolve-Path -LiteralPath $IconDirectory).Path
$destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$required = @('disconnected-light.ico', 'disconnected-dark.ico', 'window-small.ico', 'lucide-battery.ico', 'percentage-light-100.ico')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $icons $name) -PathType Leaf)) { throw "Missing icon asset: $name" }
}
Copy-Item -LiteralPath $binary -Destination (Join-Path $destination 'ControllerBatteryNative.exe') -Force
Copy-Item -Path (Join-Path $icons '*.ico') -Destination $destination -Force
$unwind = Join-Path (Split-Path -Parent $binary) 'libunwind.dll'
if (Test-Path -LiteralPath $unwind -PathType Leaf) {
    Copy-Item -LiteralPath $unwind -Destination $destination -Force
}
elseif (Test-Path -LiteralPath (Join-Path $icons 'libunwind.dll') -PathType Leaf) {
    Copy-Item -LiteralPath (Join-Path $icons 'libunwind.dll') -Destination $destination -Force
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Copy-Item -LiteralPath (Join-Path $repo 'LICENSES') -Destination (Join-Path $destination 'LICENSES') -Recurse -Force
Get-ChildItem -LiteralPath $destination -File | Measure-Object -Property Length -Sum | Select-Object Count,Sum
