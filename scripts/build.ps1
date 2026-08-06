[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$SptRoot = $env:SPT_ROOT
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projects = @(Get-ChildItem -Path (Join-Path $root 'src') -Filter 'SPTQuestMap.csproj' -Recurse -File)

if ($projects.Count -ne 1) {
    throw "Expected exactly one SPTQuestMap.csproj under src/, found $($projects.Count)."
}

if ([string]::IsNullOrWhiteSpace($SptRoot) -or -not (Test-Path -LiteralPath $SptRoot -PathType Container)) {
    throw 'Pass -SptRoot or set SPT_ROOT to the Tarkov install directory containing the SPT subfolder.'
}

$target = $projects[0].FullName
$sptRootFull = (Resolve-Path -LiteralPath $SptRoot).Path
$sptCore = Join-Path $sptRootFull 'SPT/SPTarkov.Server.Core.dll'
if (-not (Test-Path -LiteralPath $sptCore -PathType Leaf)) {
    throw "SPTarkov.Server.Core.dll was not found beneath the SPT subfolder of Tarkov install root: $sptRootFull"
}

$coreVersion = [System.Reflection.AssemblyName]::GetAssemblyName($sptCore).Version
if ($coreVersion.ToString() -ne '4.0.13.0') {
    throw "SPT-QuestMap targets SPT 4.0.13, but $sptCore reports assembly version $coreVersion."
}

$sptProperty = "-p:SptInstallRoot=$sptRootFull"

dotnet restore $target $sptProperty
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }

dotnet build $target --configuration $Configuration --no-restore $sptProperty
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

$testProjects = @(Get-ChildItem -Path (Join-Path $root 'tests') -Filter '*.csproj' -Recurse -File -ErrorAction SilentlyContinue)
foreach ($testProject in $testProjects) {
    dotnet restore $testProject.FullName $sptProperty
    if ($LASTEXITCODE -ne 0) { throw "Test restore failed with exit code $LASTEXITCODE for $($testProject.FullName)." }

    dotnet test $testProject.FullName --configuration $Configuration --no-restore $sptProperty
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE for $($testProject.FullName)." }
}

$output = Join-Path $root "src/SPTQuestMap/bin/$Configuration/net9.0"
$dist = Join-Path $root 'dist'
if (Test-Path -LiteralPath $dist) {
    $resolvedDist = (Resolve-Path -LiteralPath $dist).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
    if (-not $resolvedDist.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear deployment directory outside the workspace: $resolvedDist"
    }

    Remove-Item -LiteralPath $resolvedDist -Recurse -Force
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $output 'SPTQuestMap.dll') -Destination $dist
Copy-Item -LiteralPath (Join-Path $output 'SPTQuestMap.Core.dll') -Destination $dist
if (Test-Path -LiteralPath (Join-Path $output 'SPTQuestMap.pdb')) {
    Copy-Item -LiteralPath (Join-Path $output 'SPTQuestMap.pdb') -Destination $dist
}
if (Test-Path -LiteralPath (Join-Path $output 'SPTQuestMap.Core.pdb')) {
    Copy-Item -LiteralPath (Join-Path $output 'SPTQuestMap.Core.pdb') -Destination $dist
}
$forbiddenScripts = @(Get-ChildItem -LiteralPath $dist -Recurse -File | Where-Object { $_.Extension -in @('.js', '.ts') })
if ($forbiddenScripts.Count -gt 0) {
    throw "SPT 4.0.13 rejects server mods containing .js or .ts files: $($forbiddenScripts.FullName -join ', ')"
}

Write-Host "Validated SPT core version: $coreVersion"
Write-Host "Deployment staged at: $dist"
