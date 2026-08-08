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

$defaultServerExecutable = Join-Path $sptRootResolved 'SPT/SPT.Server.exe'
$useDefaultRestart = $dllChanged -and [string]::IsNullOrWhiteSpace($RestartCommand)
$defaultServerWasRunning = $false
if ($useDefaultRestart) {
    if (-not (Test-Path -LiteralPath $defaultServerExecutable -PathType Leaf)) {
        throw "SPT server executable was not found: $defaultServerExecutable"
    }

    $serverExecutableFull = [System.IO.Path]::GetFullPath($defaultServerExecutable)
    $serverProcesses = @(Get-Process -Name 'SPT.Server' -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and [System.IO.Path]::GetFullPath($_.Path).Equals($serverExecutableFull, [StringComparison]::OrdinalIgnoreCase)
    })
    $defaultServerWasRunning = $serverProcesses.Count -gt 0
    foreach ($serverProcess in $serverProcesses) {
        if ($PSCmdlet.ShouldProcess($serverProcess.Path, "Stop SPT server process $($serverProcess.Id) before DLL deployment")) {
            Stop-Process -Id $serverProcess.Id -Force
            Wait-Process -Id $serverProcess.Id -Timeout 30 -ErrorAction SilentlyContinue
        }
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
            $copied = $false
            for ($attempt = 1; $attempt -le 20 -and -not $copied; $attempt++) {
                try {
                    Copy-Item -LiteralPath $_.FullName -Destination $destinationFile -Force
                    $copied = $true
                }
                catch [System.IO.IOException] {
                    if ($attempt -eq 20) { throw }
                    Start-Sleep -Milliseconds 250
                }
            }
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
    if ($useDefaultRestart -and $defaultServerWasRunning) {
        if ($PSCmdlet.ShouldProcess($defaultServerExecutable, 'Start SPT server after DLL deployment')) {
            Start-Process -FilePath $defaultServerExecutable -WorkingDirectory (Split-Path -Parent $defaultServerExecutable) -WindowStyle Hidden
        }
    }
    elseif ($useDefaultRestart) {
        Write-Host 'The SPT server was not running before deployment; it was left stopped.'
    }
    elseif ($PSCmdlet.ShouldProcess('SPT server', "Run restart command: $RestartCommand")) {
        Invoke-Expression $RestartCommand
    }
}
