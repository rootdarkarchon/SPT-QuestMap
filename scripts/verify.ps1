[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost',
    [string]$Route = '/questmap'
)

$ErrorActionPreference = 'Stop'
$url = "$($BaseUrl.TrimEnd('/'))/$($Route.TrimStart('/'))"

try {
    $response = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing
    Write-Host "HTTP $($response.StatusCode): $url"
}
catch {
    Write-Error "QuestMap verification failed for $url`n$($_.Exception.Message)"
    exit 1
}
