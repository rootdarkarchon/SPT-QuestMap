[CmdletBinding()]
param(
    [ValidateSet('Client', 'Server', 'Both')]
    [string]$Target = 'Server',

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

$versionedProjects = @(
    'src/SPTQuestMap/SPTQuestMap.csproj',
    'src/SPTQuestMap.Core/SPTQuestMap.Core.csproj',
    'src/SPTQuestMap.Client/SPTQuestMap.Client.csproj'
)
foreach ($projectPath in $versionedProjects) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $root $projectPath) -Raw
    $projectVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ($projectVersion.Count -ne 1 -or $projectVersion[0] -ne $Version) {
        throw "Package version '$Version' does not match $projectPath version '$($projectVersion[0])'."
    }
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
$clientDirectory = Join-Path $packageRoot 'BepInEx/plugins/SPTQuestMap'
$archive = Join-Path $outputFull "SPT-QuestMap-$Version.zip"
$serverRequested = $Target -in @('Server', 'Both')
$clientRequested = $Target -in @('Client', 'Both')
$serverOutput = Join-Path $root 'dist/server'
$clientOutput = Join-Path $root 'dist/client'
$dll = Join-Path $serverOutput 'SPTQuestMap.dll'
$serverCoreDll = Join-Path $serverOutput 'SPTQuestMap.Core.dll'
$metaInfo = Join-Path $serverOutput 'Data/metainfo.json'
$clientDll = Join-Path $clientOutput 'SPTQuestMap.Client.dll'
$clientCoreDll = Join-Path $clientOutput 'SPTQuestMap.Core.dll'

if ($serverRequested -and -not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Staged server mod DLL was not found: $dll. Run scripts/build.ps1 -Target Server first."
}
if ($serverRequested -and -not (Test-Path -LiteralPath $serverCoreDll -PathType Leaf)) {
    throw "Built server shared core DLL was not found: $serverCoreDll"
}
if ($serverRequested -and -not (Test-Path -LiteralPath $metaInfo -PathType Leaf)) {
    throw "Staged quest metadata was not found: $metaInfo"
}
if ($clientRequested -and -not (Test-Path -LiteralPath $clientDll -PathType Leaf)) {
    throw "Staged client DLL was not found: $clientDll. Run scripts/build.ps1 -Target Client first."
}
if ($clientRequested -and -not (Test-Path -LiteralPath $clientCoreDll -PathType Leaf)) {
    throw "Built client shared core DLL was not found: $clientCoreDll"
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

if ($serverRequested) {
    New-Item -ItemType Directory -Path $modDirectory -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination $modDirectory
    Copy-Item -LiteralPath $serverCoreDll -Destination $modDirectory
    $dataDirectory = Join-Path $modDirectory 'Data'
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    Copy-Item -LiteralPath $metaInfo -Destination $dataDirectory

    $pdb = Join-Path $serverOutput 'SPTQuestMap.pdb'
    if (Test-Path -LiteralPath $pdb -PathType Leaf) {
        Copy-Item -LiteralPath $pdb -Destination $modDirectory
    }
    $serverCorePdb = Join-Path $serverOutput 'SPTQuestMap.Core.pdb'
    if (Test-Path -LiteralPath $serverCorePdb -PathType Leaf) {
        Copy-Item -LiteralPath $serverCorePdb -Destination $modDirectory
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $modDirectory
}

if ($clientRequested) {
    New-Item -ItemType Directory -Path $clientDirectory -Force | Out-Null
    Copy-Item -LiteralPath $clientDll -Destination $clientDirectory
    Copy-Item -LiteralPath $clientCoreDll -Destination $clientDirectory
    $clientPdb = Join-Path $clientOutput 'SPTQuestMap.Client.pdb'
    if (Test-Path -LiteralPath $clientPdb -PathType Leaf) {
        Copy-Item -LiteralPath $clientPdb -Destination $clientDirectory
    }
    $clientCorePdb = Join-Path $clientOutput 'SPTQuestMap.Core.pdb'
    if (Test-Path -LiteralPath $clientCorePdb -PathType Leaf) {
        Copy-Item -LiteralPath $clientCorePdb -Destination $clientDirectory
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $clientDirectory
}

if (Test-Path -LiteralPath $archive -PathType Leaf) {
    Remove-Item -LiteralPath $archive -Force
}

$archiveRoots = @()
if ($serverRequested) { $archiveRoots += Join-Path $packageRoot 'SPT' }
if ($clientRequested) { $archiveRoots += Join-Path $packageRoot 'BepInEx' }
Compress-Archive -LiteralPath $archiveRoots -DestinationPath $archive -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $requiredEntries = @()
    if ($serverRequested) {
        $requiredEntries += 'SPT/user/mods/SPT-QuestMap/SPTQuestMap.dll'
        $requiredEntries += 'SPT/user/mods/SPT-QuestMap/SPTQuestMap.Core.dll'
        $requiredEntries += 'SPT/user/mods/SPT-QuestMap/Data/metainfo.json'
    }
    if ($clientRequested) {
        $requiredEntries += 'BepInEx/plugins/SPTQuestMap/SPTQuestMap.Client.dll'
        $requiredEntries += 'BepInEx/plugins/SPTQuestMap/SPTQuestMap.Core.dll'
    }
    foreach ($requiredEntry in $requiredEntries) {
        if ($entries -notcontains $requiredEntry) {
            throw "Release archive is missing required entry '$requiredEntry'."
        }
    }

    $allowedPrefixes = @()
    if ($serverRequested) { $allowedPrefixes += 'SPT/user/mods/SPT-QuestMap/' }
    if ($clientRequested) { $allowedPrefixes += 'BepInEx/plugins/SPTQuestMap/' }
    $filesOutsideQuestMap = @($entries | Where-Object {
        if ($_.EndsWith('/')) { return $false }
        foreach ($prefix in $allowedPrefixes) {
            if ($_.StartsWith($prefix, [StringComparison]::Ordinal)) { return $false }
        }
        return $true
    })
    if ($filesOutsideQuestMap.Count -gt 0) {
        throw "Release archive contains files outside the requested QuestMap directories: $($filesOutsideQuestMap -join ', ')"
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Release archive: $archive"
Write-Host "Package target: $Target"
if ($serverRequested) { Write-Host 'Server install root: SPT/user/mods/SPT-QuestMap/' }
if ($clientRequested) { Write-Host 'Client install root: BepInEx/plugins/SPTQuestMap/' }
