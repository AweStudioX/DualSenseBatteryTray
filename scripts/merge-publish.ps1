[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AppDirectory,
    [Parameter(Mandatory)] [string] $WatcherDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-PublishDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not [System.IO.Path]::IsPathFullyQualified($Path)) {
        throw "Expected an absolute publish path: '$Path'."
    }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "Publish directory does not exist: '$resolved'."
    }
    if (([System.IO.File]::GetAttributes($resolved) -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing a reparse-point publish directory: '$resolved'."
    }
    return $resolved
}

function Assert-PublishTreeIsOrdinary {
    param([Parameter(Mandatory)] [string] $Root)

    foreach ($entry in Get-ChildItem -LiteralPath $Root -Force -Recurse) {
        if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a reparse point in a publish directory: '$($entry.FullName)'."
        }
    }
}

function Merge-PublishTree {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    foreach ($sourceFile in Get-ChildItem -LiteralPath $Source -File -Force -Recurse) {
        $relativePath = [System.IO.Path]::GetRelativePath($Source, $sourceFile.FullName)
        if ($relativePath.StartsWith('..', [System.StringComparison]::Ordinal)) {
            throw "Publish file escaped its source directory: '$($sourceFile.FullName)'."
        }

        $destinationFile = Join-Path $Destination $relativePath
        $destinationParent = Split-Path -Parent $destinationFile
        if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
            $null = New-Item -ItemType Directory -Path $destinationParent -ErrorAction Stop
        }

        if (Test-Path -LiteralPath $destinationFile) {
            if (-not (Test-Path -LiteralPath $destinationFile -PathType Leaf)) {
                throw "Publish path collision is not a file: '$relativePath'."
            }
            $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash
            if ($sourceHash -ne $destinationHash) {
                throw "Publish outputs contain different files at '$relativePath'."
            }
            continue
        }

        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationFile -ErrorAction Stop
    }
}

$appRoot = Resolve-PublishDirectory -Path $AppDirectory
$watcherRoot = Resolve-PublishDirectory -Path $WatcherDirectory
Assert-PublishTreeIsOrdinary -Root $appRoot
Assert-PublishTreeIsOrdinary -Root $watcherRoot

foreach ($required in @(
        @{ Root = $appRoot; Name = 'DualSenseBatteryTray.App.exe' },
        @{ Root = $watcherRoot; Name = 'DualSenseBatteryTray.Watcher.exe' })) {
    $path = Join-Path $required.Root $required.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).Length -le 0) {
        throw "Published executable is missing or empty: '$path'."
    }
}

if (-not [System.IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    throw "Expected an absolute output path: '$OutputDirectory'."
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
foreach ($sourceRoot in @($appRoot, $watcherRoot)) {
    $sourcePrefix = $sourceRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($outputRoot.Equals($sourceRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $outputRoot.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Output directory must not be inside a publish directory: '$outputRoot'."
    }
}

if (Test-Path -LiteralPath $outputRoot) {
    $outputItem = Get-Item -LiteralPath $outputRoot -Force
    if (-not $outputItem.PSIsContainer -or
        ($outputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Expected an ordinary output directory: '$outputRoot'."
    }
    if (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -ne 0) {
        throw "Output directory must be empty: '$outputRoot'."
    }
}
else {
    $null = New-Item -ItemType Directory -Path $outputRoot -ErrorAction Stop
}

Merge-PublishTree -Source $appRoot -Destination $outputRoot
Merge-PublishTree -Source $watcherRoot -Destination $outputRoot

Write-Host "Merged self-contained publish output into '$outputRoot'."
