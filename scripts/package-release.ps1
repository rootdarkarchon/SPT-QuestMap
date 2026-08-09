[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory)]
    [string]$Version,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a semantic version such as 1.1.0."
}

[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/SPTQuestMap/SPTQuestMap.csproj') -Raw
$projectVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($projectVersion.Count -ne 1 -or $projectVersion[0] -ne $Version) {
    throw "Package version '$Version' does not match project version '$($projectVersion[0])'."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts/release'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $root $OutputDirectory
}

$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
$packageRoot = Join-Path $outputFull 'package'
$modDirectory = Join-Path $packageRoot 'SPT/user/mods/SPT-QuestMap'
$archive = Join-Path $outputFull "SPT-QuestMap-$Version.zip"
$buildOutput = Join-Path $root 'dist/server'
$dll = Join-Path $buildOutput 'SPTQuestMap.dll'
$coreDll = Join-Path $buildOutput 'SPTQuestMap.Core.dll'

if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Staged server mod DLL was not found: $dll. Run scripts/build.ps1 -Target Server first."
}
if (-not (Test-Path -LiteralPath $coreDll -PathType Leaf)) {
    throw "Built shared core DLL was not found: $coreDll"
}

New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    $resolvedPackage = (Resolve-Path -LiteralPath $packageRoot).Path
    $requiredPrefix = $outputFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPackage.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear package staging outside the output directory: $resolvedPackage"
    }

    Remove-Item -LiteralPath $resolvedPackage -Recurse -Force
}

New-Item -ItemType Directory -Path $modDirectory -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination $modDirectory
Copy-Item -LiteralPath $coreDll -Destination $modDirectory

$pdb = Join-Path $buildOutput 'SPTQuestMap.pdb'
if (Test-Path -LiteralPath $pdb -PathType Leaf) {
    Copy-Item -LiteralPath $pdb -Destination $modDirectory
}
$corePdb = Join-Path $buildOutput 'SPTQuestMap.Core.pdb'
if (Test-Path -LiteralPath $corePdb -PathType Leaf) {
    Copy-Item -LiteralPath $corePdb -Destination $modDirectory
}

Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $modDirectory

if (Test-Path -LiteralPath $archive -PathType Leaf) {
    Remove-Item -LiteralPath $archive -Force
}

Compress-Archive -LiteralPath (Join-Path $packageRoot 'SPT') -DestinationPath $archive -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $requiredEntry = 'SPT/user/mods/SPT-QuestMap/SPTQuestMap.dll'
    if ($entries -notcontains $requiredEntry) {
        throw "Release archive is missing required entry '$requiredEntry'."
    }
    $requiredCoreEntry = 'SPT/user/mods/SPT-QuestMap/SPTQuestMap.Core.dll'
    if ($entries -notcontains $requiredCoreEntry) {
        throw "Release archive is missing required entry '$requiredCoreEntry'."
    }

    $filesOutsideModDirectory = @($entries | Where-Object {
        -not $_.EndsWith('/') -and -not $_.StartsWith('SPT/user/mods/SPT-QuestMap/', [StringComparison]::Ordinal)
    })
    if ($filesOutsideModDirectory.Count -gt 0) {
        throw "Release archive contains files outside the QuestMap mod directory: $($filesOutsideModDirectory -join ', ')"
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Release archive: $archive"
Write-Host 'Install root: SPT/user/mods/SPT-QuestMap/'
