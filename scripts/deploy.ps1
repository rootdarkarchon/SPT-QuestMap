[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$SptRoot,

    [string]$DeploymentSource,

    [string]$ModRelativePath = 'user/mods/SPT-QuestMap',

    [string]$RestartCommand,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path -LiteralPath $SptRoot -PathType Container)) {
    throw "Tarkov install root does not exist: $SptRoot"
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -SptRoot $SptRoot
}

if ([string]::IsNullOrWhiteSpace($DeploymentSource)) {
    $candidatePaths = @(
        (Join-Path $root 'dist'),
        (Join-Path $root 'artifacts/deploy')
    )
    $DeploymentSource = $candidatePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($DeploymentSource) -or -not (Test-Path -LiteralPath $DeploymentSource -PathType Container)) {
    throw 'Deployment source was not found. Update the build to emit dist/ or artifacts/deploy/, or pass -DeploymentSource.'
}

$destination = Join-Path $SptRoot (Join-Path 'SPT' $ModRelativePath)
$sourceRoot = (Resolve-Path -LiteralPath $DeploymentSource).Path
$sptRootResolved = (Resolve-Path -LiteralPath $SptRoot).Path
$destinationFull = [System.IO.Path]::GetFullPath($destination)
$requiredPrefix = $sptRootResolved.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $destinationFull.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy outside the configured Tarkov install root: $destinationFull"
}

$sourceDlls = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.dll' -Recurse -File)
$dllChanged = $false

foreach ($sourceDll in $sourceDlls) {
    $relativePath = [System.IO.Path]::GetRelativePath($sourceRoot, $sourceDll.FullName)
    $deployedDll = Join-Path $destination $relativePath

    if (-not (Test-Path -LiteralPath $deployedDll -PathType Leaf)) {
        $dllChanged = $true
        break
    }

    $newHash = (Get-FileHash -LiteralPath $sourceDll.FullName -Algorithm SHA256).Hash
    $oldHash = (Get-FileHash -LiteralPath $deployedDll -Algorithm SHA256).Hash
    if ($newHash -ne $oldHash) {
        $dllChanged = $true
        break
    }
}

if ($PSCmdlet.ShouldProcess($destination, "Deploy files from $DeploymentSource")) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($sourceRoot, $_.FullName)
        $destinationFile = Join-Path $destinationFull $relativePath
        $copyRequired = -not (Test-Path -LiteralPath $destinationFile -PathType Leaf)
        if (-not $copyRequired) {
            $sourceHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash
            $copyRequired = $sourceHash -ne $destinationHash
        }

        if ($copyRequired) {
            $destinationDirectory = Split-Path -Parent $destinationFile
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destinationFile -Force
            Write-Host "Updated deployment file: $relativePath"
        }
    }

    $sourceRelativeFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
        [void]$sourceRelativeFiles.Add([System.IO.Path]::GetRelativePath($sourceRoot, $_.FullName))
    }

    Get-ChildItem -LiteralPath $destinationFull -Recurse -File | ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($destinationFull, $_.FullName)
        $isUserSummary = $relativePath.StartsWith("Summaries$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)
        if (-not $sourceRelativeFiles.Contains($relativePath) -and -not $isUserSummary) {
            Write-Host "Removing stale deployment file: $relativePath"
            Remove-Item -LiteralPath $_.FullName -Force
        }
    }
}

Write-Host "Deployed SPT-QuestMap to: $destination"
Write-Host "DLL changed: $dllChanged"

if ($dllChanged) {
    if ([string]::IsNullOrWhiteSpace($RestartCommand)) {
        Write-Warning 'The deployed DLL changed. Restart the SPT server before testing /questmap. Supply -RestartCommand to automate this.'
    }
    elseif ($PSCmdlet.ShouldProcess('SPT server', "Run restart command: $RestartCommand")) {
        Invoke-Expression $RestartCommand
    }
}
