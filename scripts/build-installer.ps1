[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $MakensisPath,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

if ($Version -cnotmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version '$Version'; expected three numeric components."
}

foreach ($path in @($PublishDirectory, $MakensisPath, $OutputPath)) {
    if (-not [System.IO.Path]::IsPathFullyQualified($path)) {
        throw "Expected an absolute path: '$path'."
    }
}

$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Publish directory does not exist: '$publishRoot'."
}

foreach ($name in 'DualSenseBatteryTray.App.exe', 'DualSenseBatteryTray.Watcher.exe') {
    $executable = Join-Path $publishRoot $name
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Published executable is missing: '$executable'."
    }
    if ((Get-Item -LiteralPath $executable -ErrorAction Stop).Length -le 0) {
        throw "Published executable is empty: '$executable'."
    }
}

$compiler = [System.IO.Path]::GetFullPath($MakensisPath)
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "NSIS compiler does not exist: '$compiler'."
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $output
$null = New-Item -ItemType Directory -Path $outputDirectory -Force -ErrorAction Stop
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'installer\DualSenseBatteryTray.nsi'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "NSIS source does not exist: '$source'."
}

$compilerArguments = @(
    '/V2',
    "/DPROJECT_ROOT=$projectRoot",
    "/DPUBLISH_DIR=$publishRoot",
    "/DPRODUCT_VERSION=$Version",
    "/DOUTPUT_FILE=$output",
    $source
)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "NSIS compilation failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $output -PathType Leaf) -or
    (Get-Item -LiteralPath $output).Length -le 0) {
    throw "NSIS did not produce a nonempty installer: '$output'."
}

Write-Host "Built $output"
