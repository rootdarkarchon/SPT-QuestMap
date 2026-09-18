[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SptRoot,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$install = (Resolve-Path -LiteralPath $SptRoot).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts/private-build-references'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$serverCore = Join-Path $install 'SPT_Runtime/SPTarkov.Server.Core.dll'
$eftAssembly = Join-Path $install 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
if ((Get-FileHash -LiteralPath $serverCore -Algorithm SHA256).Hash -ne '490409F7C67480A8BF647DA67ADA8B91361330C2A9324D9E9787CD1EBAF5BF27' -or
    (Get-FileHash -LiteralPath $eftAssembly -Algorithm SHA256).Hash -ne 'EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436') {
    throw 'Reference input must match the pinned SPT 4.1.6 / EFT 40743 build.'
}

function New-ReferenceArchive([string]$Name, [System.IO.FileInfo[]]$Files) {
    $path = Join-Path $output $Name
    # Create overwrites only this named archive; no directory is cleared and no user data is collected.
    $stream = [IO.File]::Open($path, [IO.FileMode]::Create)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $Files | Sort-Object FullName -Unique) {
            $relative = [IO.Path]::GetRelativePath($install, $file.FullName).Replace('\', '/')
            if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) {
                throw "Reference outside installation: $($file.FullName)"
            }
            $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]'2000-01-01T00:00:00Z'
            $source = $file.OpenRead()
            $destination = $entry.Open()
            try { $source.CopyTo($destination) }
            finally { $destination.Dispose(); $source.Dispose() }
        }
    }
    finally { $zip.Dispose(); $stream.Dispose() }
    [pscustomobject]@{ File = $path; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; Bytes = (Get-Item -LiteralPath $path).Length }
}

$serverFiles = @(Get-ChildItem -LiteralPath (Join-Path $install 'SPT_Runtime') -File -Filter '*.dll')
$clientFiles = @(Get-Item -LiteralPath (Join-Path $install 'EscapeFromTarkov.exe'))
foreach ($relative in @('EscapeFromTarkov_Data/Managed', 'BepInEx/core', 'BepInEx/plugins/spt')) {
    $clientFiles += @(Get-ChildItem -LiteralPath (Join-Path $install $relative) -File -Filter '*.dll')
}
$manifest = @(
    New-ReferenceArchive 'eft-spt-4.1.6-build-references.zip' ($serverFiles + $clientFiles)
)
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
$manifest | Format-Table -AutoSize
Write-Host 'Private CI input only. Set EFT_REFERENCE_ARCHIVE_4_1_URL and EFT_REFERENCE_ARCHIVE_4_1_SHA256; never attach this ZIP to a QuestMap release.'
