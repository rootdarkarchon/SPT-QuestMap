[CmdletBinding()]
param(
    [ValidateSet('Client', 'Server', 'Both')]
    [string]$Target = 'Both',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$SptRoot = $env:SPT_ROOT,

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$clientRequested = $Target -in @('Client', 'Both')
$serverRequested = $Target -in @('Server', 'Both')

if ([string]::IsNullOrWhiteSpace($SptRoot) -or -not (Test-Path -LiteralPath $SptRoot -PathType Container)) {
    throw 'Pass -SptRoot or set SPT_ROOT to the Tarkov install directory containing BepInEx and SPT.'
}

$sptRootFull = (Resolve-Path -LiteralPath $SptRoot).Path
$serverCore = Join-Path $sptRootFull 'SPT/SPTarkov.Server.Core.dll'
$clientExecutable = Join-Path $sptRootFull 'EscapeFromTarkov.exe'
if ($serverRequested -and -not (Test-Path -LiteralPath $serverCore -PathType Leaf)) {
    throw "SPTarkov.Server.Core.dll was not found beneath the SPT subfolder of Tarkov install root: $sptRootFull"
}
if ($clientRequested -and -not (Test-Path -LiteralPath $clientExecutable -PathType Leaf)) {
    throw "EscapeFromTarkov.exe was not found beneath the Tarkov install root: $sptRootFull"
}
if (Test-Path -LiteralPath $serverCore -PathType Leaf) {
    $coreVersion = [System.Reflection.AssemblyName]::GetAssemblyName($serverCore).Version
    if ($coreVersion.ToString() -ne '4.0.13.0') {
        throw "SPT-QuestMap targets SPT 4.0.13, but $serverCore reports assembly version $coreVersion."
    }
    Write-Host "Validated SPT core version: $coreVersion"
}

$clientProject = Join-Path $root 'src/SPTQuestMap.Client/SPTQuestMap.Client.csproj'
$serverProject = Join-Path $root 'src/SPTQuestMap/SPTQuestMap.csproj'
$coreTestProject = Join-Path $root 'tests/SPTQuestMap.Core.Tests/SPTQuestMap.Core.Tests.csproj'
$serverTestProject = Join-Path $root 'tests/SPTQuestMap.Tests/SPTQuestMap.Tests.csproj'
$artifactRoot = Join-Path $root 'artifacts/build'
$testArtifactRoot = Join-Path $root 'artifacts/tests'
$distRoot = Join-Path $root 'dist'
$configurationFolder = $Configuration.ToLowerInvariant()

function Invoke-DotNet([string[]]$Arguments, [string]$FailureMessage) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FailureMessage (exit code $LASTEXITCODE)." }
}

function Reset-ScopedDirectory([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = [System.IO.Path]::GetFullPath($root).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear build directory outside the workspace: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) { Remove-Item -LiteralPath $fullPath -Recurse -Force }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Copy-StagedFile([string]$Source, [string]$DestinationDirectory, [switch]$Optional) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        if ($Optional) { return }
        throw "Expected build artifact was not found: $Source"
    }
    Copy-Item -LiteralPath $Source -Destination $DestinationDirectory -Force
}

# --artifacts-path is the supported SDK mechanism for collision-free per-project
# bin/obj output. Do not override BaseIntermediateOutputPath: that can make stale
# generated assembly attributes part of a subsequent compilation.
Reset-ScopedDirectory $artifactRoot
if ($clientRequested) {
    Invoke-DotNet @('build', $clientProject, '--configuration', $Configuration, '--artifacts-path', $artifactRoot,
        "-p:EftInstallRoot=$sptRootFull", '-m:1') 'Client build failed'
}
if ($serverRequested) {
    Invoke-DotNet @('build', $serverProject, '--configuration', $Configuration, '--artifacts-path', $artifactRoot,
        "-p:SptInstallRoot=$sptRootFull", '-m:1') 'Server build failed'
}

if (-not $SkipTests) {
    Reset-ScopedDirectory $testArtifactRoot
    Invoke-DotNet @('test', $coreTestProject, '--configuration', $Configuration, '--artifacts-path', $testArtifactRoot,
        "-p:SptInstallRoot=$sptRootFull", '-m:1') 'Shared-core tests failed'
    if ($serverRequested) {
        Invoke-DotNet @('test', $serverTestProject, '--configuration', $Configuration, '--artifacts-path', $testArtifactRoot,
            "-p:SptInstallRoot=$sptRootFull", '-m:1') 'Server/browser tests failed'
    }
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$coreOutput = Join-Path $artifactRoot "bin/SPTQuestMap.Core/$configurationFolder"
if ($clientRequested) {
    $clientStage = Join-Path $distRoot 'client'
    Reset-ScopedDirectory $clientStage
    $clientOutput = Join-Path $artifactRoot "bin/SPTQuestMap.Client/$configurationFolder"
    Copy-StagedFile (Join-Path $clientOutput 'SPTQuestMap.Client.dll') $clientStage
    Copy-StagedFile (Join-Path $clientOutput 'SPTQuestMap.Client.pdb') $clientStage -Optional
    Copy-StagedFile (Join-Path $coreOutput 'SPTQuestMap.Core.dll') $clientStage
    Copy-StagedFile (Join-Path $coreOutput 'SPTQuestMap.Core.pdb') $clientStage -Optional
    Write-Host "Client deployment staged at: $clientStage"
}
if ($serverRequested) {
    $serverStage = Join-Path $distRoot 'server'
    Reset-ScopedDirectory $serverStage
    $serverOutput = Join-Path $artifactRoot "bin/SPTQuestMap/$configurationFolder"
    Copy-StagedFile (Join-Path $serverOutput 'SPTQuestMap.dll') $serverStage
    Copy-StagedFile (Join-Path $serverOutput 'SPTQuestMap.pdb') $serverStage -Optional
    Copy-StagedFile (Join-Path $coreOutput 'SPTQuestMap.Core.dll') $serverStage
    Copy-StagedFile (Join-Path $coreOutput 'SPTQuestMap.Core.pdb') $serverStage -Optional
    $forbiddenScripts = @(Get-ChildItem -LiteralPath $serverStage -Recurse -File | Where-Object { $_.Extension -in @('.js', '.ts') })
    if ($forbiddenScripts.Count -gt 0) {
        throw "SPT 4.0.13 rejects server mods containing .js or .ts files: $($forbiddenScripts.FullName -join ', ')"
    }
    Write-Host "Server deployment staged at: $serverStage"
}

Write-Host "Build target completed: $Target"
