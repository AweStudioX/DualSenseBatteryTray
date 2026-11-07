param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$MakensisPath,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be MAJOR.MINOR.PATCH.' }
foreach ($path in @($PublishDirectory, $MakensisPath, $OutputPath)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw "Expected an absolute path: $path" }
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$exe = Join-Path $publish 'ControllerBatteryNative.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing $exe" }
$compiler = (Resolve-Path -LiteralPath $MakensisPath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
& $compiler '/V2' "/DPROJECT_ROOT=$root" "/DPUBLISH_DIR=$publish" "/DPRODUCT_VERSION=$Version" "/DOUTPUT_FILE=$output" (Join-Path $root 'installer\ControllerBatteryNative.nsi')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Native NSIS build failed.' }
