[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Client', 'Server', 'Both')]
    [string]$Target = 'Both',

    [string]$SptRoot = $env:SPT_ROOT,

    [string]$DeploymentSource,

    [string]$ModRelativePath = 'user/mods/SPT-QuestMap',

    [string]$ClientRelativePath = 'BepInEx/plugins/SPTQuestMap',

    [string]$RestartCommand,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipBuild,

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$clientRequested = $Target -in @('Client', 'Both')
$serverRequested = $Target -in @('Server', 'Both')

if ([string]::IsNullOrWhiteSpace($SptRoot) -or -not (Test-Path -LiteralPath $SptRoot -PathType Container)) {
    throw 'Pass -SptRoot or set SPT_ROOT to the Tarkov install directory containing BepInEx and SPT.'
}
$sptRootResolved = (Resolve-Path -LiteralPath $SptRoot).Path

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Target $Target -Configuration $Configuration -SptRoot $sptRootResolved -SkipTests:$SkipTests
    if ($LASTEXITCODE -ne 0) { throw "Build script failed with exit code $LASTEXITCODE." }
}

if ([string]::IsNullOrWhiteSpace($DeploymentSource)) {
    $DeploymentSource = Join-Path $root 'dist'
}
if (-not (Test-Path -LiteralPath $DeploymentSource -PathType Container)) {
    throw "Deployment source was not found: $DeploymentSource"
}
$sourceRoot = (Resolve-Path -LiteralPath $DeploymentSource).Path
$requiredPrefix = $sptRootResolved.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

function Resolve-Stage([string]$Name) {
    $child = Join-Path $sourceRoot $Name.ToLowerInvariant()
    if (Test-Path -LiteralPath $child -PathType Container) { return (Resolve-Path -LiteralPath $child).Path }
    if ($Target -ne 'Both') { return $sourceRoot }
    throw "The '$Name' staging directory was not found below: $sourceRoot"
}

function Resolve-SafeDestination([string]$RelativePath) {
    $destination = [System.IO.Path]::GetFullPath((Join-Path $sptRootResolved $RelativePath))
    if (-not $destination.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to deploy outside the configured Tarkov install root: $destination"
    }
    return $destination
}

function Test-DllChanged([string]$Stage, [string]$Destination) {
    foreach ($sourceDll in Get-ChildItem -LiteralPath $Stage -Filter '*.dll' -Recurse -File) {
        $relativePath = [System.IO.Path]::GetRelativePath($Stage, $sourceDll.FullName)
        $deployedDll = Join-Path $Destination $relativePath
        if (-not (Test-Path -LiteralPath $deployedDll -PathType Leaf)) { return $true }
        if ((Get-FileHash -LiteralPath $sourceDll.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash) { return $true }
    }
    return $false
}

function Sync-Stage([string]$Stage, [string]$Destination, [switch]$PreserveSummaries) {
    if (-not $PSCmdlet.ShouldProcess($Destination, "Deploy files from $Stage")) { return }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $sourceFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($sourceFile in Get-ChildItem -LiteralPath $Stage -Recurse -File) {
        $relativePath = [System.IO.Path]::GetRelativePath($Stage, $sourceFile.FullName)
        [void]$sourceFiles.Add($relativePath)
        $destinationFile = Join-Path $Destination $relativePath
        $copyRequired = -not (Test-Path -LiteralPath $destinationFile -PathType Leaf)
        if (-not $copyRequired) {
            $copyRequired = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash
        }
        if ($copyRequired) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFile) -Force | Out-Null
            Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationFile -Force
            Write-Host "Updated deployment file: $relativePath"
        }
        if ((Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash) {
            throw "Deployment hash verification failed: $destinationFile"
        }
    }
    foreach ($deployedFile in Get-ChildItem -LiteralPath $Destination -Recurse -File) {
        $relativePath = [System.IO.Path]::GetRelativePath($Destination, $deployedFile.FullName)
        $isPreservedSummary = $PreserveSummaries -and
            $relativePath.StartsWith("summaries$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)
        if (-not $sourceFiles.Contains($relativePath) -and -not $isPreservedSummary) {
            Remove-Item -LiteralPath $deployedFile.FullName -Force
            Write-Host "Removed stale deployment file: $relativePath"
        }
    }
}

$serverStage = $null
$serverDestination = $null
$serverChanged = $false
if ($serverRequested) {
    $serverStage = Resolve-Stage 'Server'
    $serverDestination = Resolve-SafeDestination (Join-Path 'SPT' $ModRelativePath)
    $serverChanged = Test-DllChanged $serverStage $serverDestination
}

$serverExecutable = Join-Path $sptRootResolved 'SPT/SPT.Server.exe'
if ($serverChanged -and [string]::IsNullOrWhiteSpace($RestartCommand)) {
    if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) {
        throw "SPT server executable was not found: $serverExecutable"
    }
    $serverExecutableFull = [System.IO.Path]::GetFullPath($serverExecutable)
    foreach ($serverProcess in @(Get-Process -Name 'SPT.Server' -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and [System.IO.Path]::GetFullPath($_.Path).Equals($serverExecutableFull, [StringComparison]::OrdinalIgnoreCase)
    })) {
        if ($PSCmdlet.ShouldProcess($serverProcess.Path, "Stop SPT server process $($serverProcess.Id) before DLL deployment")) {
            Stop-Process -Id $serverProcess.Id -Force
            Wait-Process -Id $serverProcess.Id -Timeout 30 -ErrorAction SilentlyContinue
        }
    }
}

if ($clientRequested) {
    $clientStage = Resolve-Stage 'Client'
    $clientDestination = Resolve-SafeDestination $ClientRelativePath
    # Client deployment deliberately does not inspect, stop, or launch EFT.
    Sync-Stage $clientStage $clientDestination
    Write-Host "Deployed client to: $clientDestination"
}
if ($serverRequested) {
    Sync-Stage $serverStage $serverDestination -PreserveSummaries
    Write-Host "Deployed server to: $serverDestination"
    Write-Host "Server DLL changed: $serverChanged"
}

if ($serverChanged) {
    if (-not [string]::IsNullOrWhiteSpace($RestartCommand)) {
        if ($PSCmdlet.ShouldProcess('SPT server', "Run restart command: $RestartCommand")) {
            Invoke-Expression $RestartCommand
        }
    }
    elseif ($PSCmdlet.ShouldProcess($serverExecutable, 'Start visible SPT server after DLL deployment')) {
        # The visible console is intentional: the user uses it to observe server state.
        Start-Process -FilePath $serverExecutable -WorkingDirectory (Split-Path -Parent $serverExecutable)
    }
}

Write-Host "Deployment target completed: $Target"
